using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Infrastructure.Bank;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// The Organizer's "Send marked to Bank": <see cref="BulkSlotActionsController.SendMarkedToBankAsync"/>
/// over a real <see cref="FileBankService"/> on a temp directory. Covers the move itself (box and party,
/// with party compaction), skip-and-report for protected slots, the party refusal, cancel, the
/// all-or-nothing rollback when a Bank write fails mid-batch, and that the deposits are held until the
/// save is written.
/// </summary>
public sealed class OrganizerBankTests : IDisposable
{
    private readonly FakeAppPaths _paths = new();
    private readonly List<string> _messages = [];
    private readonly List<string> _errors = [];
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly Mock<ISaveBackupService> _backups = new();
    private bool _confirm = true;

    public OrganizerBankTests()
    {
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => _confirm);
        _dialogs.Setup(d => d.ShowInformationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => _messages.Add(message))
            .Returns(Task.CompletedTask);
        _dialogs.Setup(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => _errors.Add(message))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_paths.DataDirectory)!, recursive: true); }
        catch { /* best effort */ }
    }

    private static string T(string key) => LocalizedStrings.Instance[key];
    private static string F(string key, params object[] args) => LocalizedStrings.Instance.Format(key, args);

    private (BulkSlotActionsController Controller, BoxSelectionService Selection, UndoRedoService Undo) Create(SaveFile sav, IBankService? bank) =>
        Create(sav, bank is null ? null : new BankHoldTracker(bank));

    private (BulkSlotActionsController Controller, BoxSelectionService Selection, UndoRedoService Undo) Create(SaveFile sav, BankHoldTracker? holds)
    {
        var undo = new UndoRedoService();
        undo.Initialize(sav);
        var selection = new BoxSelectionService();
        var controller = new BulkSlotActionsController(
            sav, undo, selection, _dialogs.Object, _backups.Object, new AppSettings(), () => "/tmp/organizer-bank-test.sav",
            holds, () => "Test Save");
        return (controller, selection, undo);
    }

    private static SAV6XY SaveWithParty(params ushort[] party)
    {
        var sav = new SAV6XY();
        for (var i = 0; i < party.Length; i++)
            sav.SetPartySlotAtIndex(new PK6 { Species = party[i] }, i);
        return sav;
    }

    [Fact]
    public async Task SendsMarkedBoxAndPartyPokemonToTheBank_AndCompactsTheParty()
    {
        var sav = SaveWithParty(1, 4, 7);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 133 }, 1, 5);
        var bank = new FileBankService(_paths);
        var holds = new BankHoldTracker(bank);
        var (controller, selection, undo) = Create(sav, holds);
        var completed = 0;
        controller.Completed += () => completed++;
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(1, 5));
        selection.Toggle(SlotLocation.FromParty(0));
        selection.Toggle(SlotLocation.FromParty(2));

        await controller.SendMarkedToBankAsync();

        Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(0, sav.GetBoxSlotAtIndex(1, 5).Species);
        Assert.Equal(1, sav.PartyCount);
        Assert.Equal(4, sav.GetPartySlotAtIndex(0).Species);
        Assert.True(sav.State.Edited);

        var stored = bank.GetAll();
        Assert.Equal(new ushort[] { 1, 7, 25, 133 }, stored.Select(e => e.Info.Species).Order().ToArray());
        Assert.All(stored, e => Assert.Equal("Test Save", e.Info.SourceLabel));
        Assert.All(stored, e => Assert.True(e.IsPendingDeposit)); // held until the save is written

        var outcomes = holds.OnSaveWritten(sav, sav.Clone());
        Assert.Equal(4, outcomes.Count(o => o.Resolution == BankHoldResolution.DepositCompleted));
        Assert.All(bank.GetAll(), e => Assert.Null(e.Pending));

        Assert.Equal(0, selection.Count);
        Assert.Equal(1, completed);
        Assert.Equal(0, undo.ChangeCount); // like the single deposit: undo would duplicate
        _backups.Verify(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()), Times.Once);
        Assert.Equal(F("Organizer_ResultSentToBank", 4), Assert.Single(_messages));
    }

    [Fact]
    public async Task SkipsLockedSlots_AndReportsThem()
    {
        var sav = new SAV7SM();
        sav.BoxLayout.ClearBattleTeams();
        sav.BoxLayout.TeamSlots[0] = 0;
        sav.BoxLayout.SetIsTeamLocked(0, true);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 1 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 4 }, 0, 5);
        var bank = new FileBankService(_paths);
        var (controller, selection, _) = Create(sav, bank);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 5));

        await controller.SendMarkedToBankAsync();

        Assert.Equal(1, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 5).Species);
        Assert.Equal(4, Assert.Single(bank.GetAll()).Info.Species);
        Assert.Equal(F("Organizer_ResultSentToBank", 1) + " " + F("Organizer_SkippedUnavailable", 1), Assert.Single(_messages));
        Assert.True(selection.IsMarked(SlotLocation.FromBox(0, 0))); // what stayed is still marked
    }

    [Fact]
    public async Task RefusesWhenThePartyWouldKeepOnlyEggs()
    {
        var sav = SaveWithParty(1);
        sav.SetPartySlotAtIndex(new PK6 { Species = 4, IsEgg = true }, 1);
        var bank = new FileBankService(_paths);
        var (controller, selection, _) = Create(sav, bank);
        selection.Toggle(SlotLocation.FromParty(0));

        await controller.SendMarkedToBankAsync();

        Assert.Equal(2, sav.PartyCount);
        Assert.Empty(bank.GetAll());
        Assert.Equal(T("Bank_DepositLastPartyMember") + " " + T("Organizer_NothingChangedMessage"), Assert.Single(_messages));
        _dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_ChangesNothing()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        var bank = new FileBankService(_paths);
        var (controller, selection, _) = Create(sav, bank);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        _confirm = false;

        await controller.SendMarkedToBankAsync();

        Assert.Equal(25, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Empty(bank.GetAll());
        Assert.Equal(1, selection.Count);
        _backups.Verify(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ABankFailureMidBatch_RemovesWhatWasAdded_AndLeavesTheSaveUntouched()
    {
        var sav = SaveWithParty(1, 4);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 133 }, 0, 1);
        var before = sav.Write().ToArray();

        var real = new FileBankService(_paths);
        var bank = new Mock<IBankService>();
        var adds = 0;
        bank.Setup(b => b.AddPendingDeposit(It.IsAny<byte[]>(), It.IsAny<BankEntryInfo>(), It.IsAny<BankPendingWithdrawal>()))
            .Returns((byte[] raw, BankEntryInfo info, BankPendingWithdrawal hold) =>
                ++adds == 2 ? BankAddResult.Fail("disk full") : real.AddPendingDeposit(raw, info, hold));
        bank.Setup(b => b.CancelPendingDeposit(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Returns((Guid id, Guid token) => real.CancelPendingDeposit(id, token));
        bank.Setup(b => b.Find(It.IsAny<Guid>())).Returns((Guid id) => real.Find(id));

        var (controller, selection, _) = Create(sav, bank.Object);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 1));
        selection.Toggle(SlotLocation.FromParty(1));

        await controller.SendMarkedToBankAsync();

        Assert.Empty(real.GetAll());
        Assert.Equal(before, sav.Write().ToArray());
        Assert.Single(_errors);
        Assert.EndsWith(T("Organizer_NothingChangedMessage"), _errors[0]);
        Assert.Equal(3, selection.Count);
    }

    [Fact]
    public void Viewers_OfferTheActionOnlyWhenABankExists()
    {
        var sav = new SAV6XY();
        var (withBank, selection, _) = Create(sav, new FileBankService(_paths));
        var (withoutBank, _, _) = Create(sav, (BankHoldTracker?)null);

        Assert.True(new BoxViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withBank).CanBulkSendToBank);
        Assert.True(new PartyViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withBank).CanBulkSendToBank);
        Assert.False(new BoxViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withoutBank).CanBulkSendToBank);
    }
}
