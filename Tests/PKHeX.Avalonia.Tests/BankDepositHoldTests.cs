using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Infrastructure.Bank;
using PKHeX.Presentation.Localization;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// The Bank's <b>hold until saved</b> contract for deposits: a Pokémon sent to the Bank is held there
/// until its save is written with the slot cleared; closing, switching or discarding the save cancels
/// the deposit (the Pokémon is still in the file on disk); an abandoned hold is kept on the next run.
/// Covers the storage service, the tracker (including a close during a running write) and the real
/// composed app.
/// </summary>
public sealed class BankDepositHoldTests : IDisposable
{
    private readonly FakeAppPaths _paths = new();
    private readonly string _savePath = Path.Combine(Path.GetTempPath(), $"pkhex-deposit-{Guid.NewGuid():N}.sav");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_paths.DataDirectory)!, recursive: true); }
        catch { /* best effort */ }
        try { File.Delete(_savePath); }
        catch { /* best effort */ }
    }

    private static string L(string key) => LocalizedStrings.Instance[key];

    private static PKM Mon(SaveFile sav, ushort species, string nickname)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        pk.IsNicknamed = true;
        pk.CurrentLevel = 30;
        pk.RefreshChecksum();
        return pk;
    }

    private static BankPendingWithdrawal DepositHold(Guid token) =>
        new(token, "Test Save", null, 0, 0, DateTimeOffset.UtcNow, Kind: BankHoldKind.Deposit);

    private static (byte[] Raw, BankEntryInfo Info) Make(ushort species)
    {
        var pk = new PK6 { Species = species };
        return (BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "test"));
    }

    // ------------------------------------------------------------------
    // Storage service
    // ------------------------------------------------------------------

    [Fact]
    public void PendingDeposit_BlocksRemoval_ConfirmKeepsIt_CancelRemovesIt()
    {
        var bank = new FileBankService(_paths);
        var (raw, info) = Make(25);
        var token = Guid.NewGuid();
        var entry = bank.AddPendingDeposit(raw, info, DepositHold(token) with { Kind = BankHoldKind.Withdrawal }).Entry!;

        Assert.True(entry.IsPendingDeposit); // the service forces the kind
        Assert.Equal(Environment.ProcessId, entry.Pending!.OwnerProcessId);
        Assert.Equal(BankErrorKind.PendingDeposit, bank.Remove(entry.Id).Kind);
        Assert.False(bank.CompletePendingWithdrawal(entry.Id, token).Success); // a deposit is not a withdrawal

        Assert.False(bank.CancelPendingDeposit(entry.Id, Guid.NewGuid()).Success);
        Assert.True(bank.ConfirmPendingDeposit(entry.Id, token).Success);
        Assert.Null(bank.Find(entry.Id)!.Pending);
        Assert.False(bank.CancelPendingDeposit(entry.Id, token).Success); // confirmed: never removed afterwards

        var second = bank.AddPendingDeposit(raw, info, DepositHold(token)).Entry!;
        Assert.True(bank.CancelPendingDeposit(second.Id, token).Success);
        Assert.Null(bank.Find(second.Id));
        Assert.Null(bank.GetRawData(second.Id));
    }

    [Fact]
    public void Restart_KeepsAnAbandonedDeposit()
    {
        var first = new FileBankService(_paths);
        var (raw, info) = Make(7);
        var entry = first.AddPendingDeposit(raw, info, DepositHold(Guid.NewGuid())).Entry!;

        var restarted = new FileBankService(_paths);

        var kept = restarted.Find(entry.Id);
        Assert.NotNull(kept);
        Assert.Null(kept!.Pending);
        Assert.Equal(raw, restarted.GetRawData(entry.Id));
        Assert.Contains(restarted.Warnings, w => w.Contains("deposit", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIndexWrittenBeforeDepositHolds_ReadsItsHoldsAsWithdrawals()
    {
        var bank = new FileBankService(_paths);
        var (raw, info) = Make(1);
        var entry = bank.Add(raw, info).Entry!;
        bank.MarkPendingWithdrawal(entry.Id, DepositHold(Guid.NewGuid()));
        var index = Path.Combine(_paths.DataDirectory, "bank", "index.json");
        var root = JsonNode.Parse(File.ReadAllText(index))!;
        Assert.Equal(1, StripHoldKinds(root)); // the index as an older build wrote it
        File.WriteAllText(index, root.ToJsonString());
        Assert.DoesNotContain("\"Kind\"", File.ReadAllText(index));

        var reread = new FileBankService(_paths);
        reread.Reload();

        Assert.False(reread.Find(entry.Id)!.IsPendingDeposit);
    }

    private static int StripHoldKinds(JsonNode? node)
    {
        var removed = 0;
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("Token") && obj.Remove("Kind"))
                removed++;
            foreach (var (_, child) in obj.ToList())
                removed += StripHoldKinds(child);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                removed += StripHoldKinds(child);
        }
        return removed;
    }

    // ------------------------------------------------------------------
    // Tracker
    // ------------------------------------------------------------------

    private static (SAV6XY Save, FileBankService Bank, BankHoldTracker Holds, Guid Id) DepositFromBox(FakeAppPaths paths)
    {
        var sav = new SAV6XY();
        var pk = new PK6 { Species = 25 };
        sav.SetBoxSlotAtIndex(pk, 0, 0);
        var bank = new FileBankService(paths);
        var holds = new BankHoldTracker(bank);
        var id = holds.Deposit(sav, sav.GetBoxSlotAtIndex(0, 0), SlotLocation.FromBox(0, 0), "Test Save", null).Entry!.Id;
        return (sav, bank, holds, id);
    }

    [Fact]
    public void AWriteSnapshottedBeforeTheDeposit_LeavesItPending_TheNextWriteConfirmsIt()
    {
        var (sav, bank, holds, id) = DepositFromBox(_paths);
        var before = sav.Clone(); // still holds the Pokémon
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);

        Assert.Empty(holds.OnSaveWritten(sav, before));
        Assert.True(bank.Find(id)!.IsPendingDeposit);

        var outcome = Assert.Single(holds.OnSaveWritten(sav, sav.Clone()));
        Assert.Equal(BankHoldResolution.DepositCompleted, outcome.Resolution);
        Assert.Null(bank.Find(id)!.Pending);
        Assert.Empty(holds.HeldEntryIds);
    }

    [Fact]
    public void EndingTheSession_CancelsTheDeposit()
    {
        var (sav, bank, holds, id) = DepositFromBox(_paths);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);

        var outcome = Assert.Single(holds.OnSaveSessionChanged(null));

        Assert.Equal(BankHoldResolution.DepositCancelled, outcome.Resolution);
        Assert.Null(bank.Find(id));
    }

    [Fact]
    public void ClosingDuringAWrite_LetsThatWriteDecide()
    {
        // The write had the slot cleared: the deposit is confirmed even though the session ended first.
        var (sav, bank, holds, id) = DepositFromBox(_paths);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);
        holds.OnSaveWriteStarted(sav);
        Assert.Empty(holds.OnSaveSessionChanged(null));
        Assert.True(bank.Find(id)!.IsPendingDeposit);

        Assert.Equal(BankHoldResolution.DepositCompleted, Assert.Single(holds.OnSaveWritten(sav, sav.Clone())).Resolution);
        Assert.NotNull(bank.Find(id));
    }

    [Fact]
    public void ClosingDuringAWriteThatStillHadThePokemon_CancelsOnceThatWriteLands()
    {
        var (sav, bank, holds, id) = DepositFromBox(_paths);
        var before = sav.Clone();
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);
        holds.OnSaveWriteStarted(sav);
        holds.OnSaveSessionChanged(null);

        Assert.Equal(BankHoldResolution.DepositCancelled, Assert.Single(holds.OnSaveWritten(sav, before)).Resolution);
        Assert.Null(bank.Find(id));
    }

    [Fact]
    public void ClosingDuringAWriteThatFails_CancelsTheDeposit()
    {
        var (sav, bank, holds, id) = DepositFromBox(_paths);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);
        holds.OnSaveWriteStarted(sav);
        holds.OnSaveSessionChanged(null);

        Assert.Equal(BankHoldResolution.DepositCancelled, Assert.Single(holds.OnSaveWriteFailed(sav)).Resolution);
        Assert.Null(bank.Find(id));
    }

    [Fact]
    public void PartyDeposit_CountsIdenticalCopies_SoAClonedPartnerDoesNotBlockConfirmation()
    {
        var sav = new SAV6XY();
        var clone = new PK6 { Species = 133 };
        sav.SetPartySlotAtIndex(clone, 0);
        sav.SetPartySlotAtIndex(clone.Clone(), 1);
        var bank = new FileBankService(_paths);
        var holds = new BankHoldTracker(bank);
        var id = holds.Deposit(sav, sav.GetPartySlotAtIndex(1), SlotLocation.FromParty(1), "Test Save", null).Entry!.Id;
        var before = sav.Clone();
        sav.DeletePartySlot(1);

        Assert.Empty(holds.OnSaveWritten(sav, before)); // both copies still there: not yet written
        Assert.Equal(BankHoldResolution.DepositCompleted, Assert.Single(holds.OnSaveWritten(sav, sav.Clone())).Resolution);
        Assert.NotNull(bank.Find(id));
    }

    // ------------------------------------------------------------------
    // Composed app
    // ------------------------------------------------------------------

    private (HeadlessAppFixture App, SaveFile Save, IBankService Bank) Boot()
    {
        // B2W2 rather than SV: blank SV saves carry untyped blocks that can't be re-read from disk.
        var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.B2);
        app.LoadSaveInstance(save, _savePath);
        return (app, save, app.Services.GetRequiredService<IBankService>());
    }

    [AvaloniaFact]
    public async Task SendToBank_IsHeldUntilSaved_ThenTheWrittenFileNoLongerHasIt()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetBoxSlotAtIndex(Mon(save, 143, "Snorlax"), 0, 4);

        await app.ViewModel.SendToBankAsync(SlotLocation.FromBox(0, 4));

        var entry = Assert.Single(bank.GetAll());
        Assert.True(entry.IsPendingDeposit);
        Assert.Contains(entry.Id, app.ViewModel.BankHolds!.HeldEntryIds);

        // A pending deposit cannot be withdrawn back out.
        await app.ViewModel.WithdrawAsync(entry.Id);
        Assert.Equal(L("Bank_Error_PendingDeposit"), Assert.Single(app.Dialogs.Errors).Message);

        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();

        Assert.Null(bank.Find(entry.Id)!.Pending);
        Assert.Empty(app.ViewModel.BankHolds.HeldEntryIds);
        var onDisk = Assert.IsAssignableFrom<SaveFile>(FileUtil.GetSupportedFile(_savePath));
        Assert.Equal(0, onDisk.GetBoxSlotAtIndex(0, 4).Species);
    }

    [AvaloniaFact]
    public async Task ClosingWithoutSaving_TakesTheDepositBackOutOfTheBank()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetBoxSlotAtIndex(Mon(save, 25, "Sparky"), 0, 0);
        await app.ViewModel.SendToBankAsync(SlotLocation.FromBox(0, 0));
        var id = Assert.Single(bank.GetAll()).Id;

        app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
        app.Pump();

        Assert.Null(app.Save);
        Assert.Empty(bank.GetAll());
        Assert.Null(bank.GetRawData(id));
        Assert.False(File.Exists(_savePath)); // nothing was written, so the Pokémon only ever left in memory
    }

    [AvaloniaFact]
    public async Task SwitchingToAnotherSave_CancelsTheDeposit()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetBoxSlotAtIndex(Mon(save, 4, "Ember"), 0, 0);
        await app.ViewModel.SendToBankAsync(SlotLocation.FromBox(0, 0));
        Assert.Single(bank.GetAll());

        app.LoadSaveInstance(BlankSaveFile.Get(GameVersion.B2));

        Assert.Empty(bank.GetAll());
        Assert.Empty(app.ViewModel.BankHolds!.HeldEntryIds);
    }

    [AvaloniaFact]
    public async Task RestartingWhileADepositIsPending_KeepsIt()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetBoxSlotAtIndex(Mon(save, 7, "Shell"), 0, 0);
        await app.ViewModel.SendToBankAsync(SlotLocation.FromBox(0, 0));
        var id = Assert.Single(bank.GetAll()).Id;

        // The next app run, as after a crash: a fresh Bank service with no tracker state.
        var nextRun = new FileBankService(app.Services.GetRequiredService<IAppPaths>());

        var entry = nextRun.Find(id);
        Assert.NotNull(entry);
        Assert.Null(entry!.Pending);
        Assert.NotNull(nextRun.GetRawData(id));
    }
}
