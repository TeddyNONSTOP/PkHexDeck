using System;
using System.IO;
using System.Linq;
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
/// End-to-end coverage of the Bank's <b>hold until saved</b> withdrawal contract and of deposits,
/// through the real composed app (production DI graph: real <c>SaveFileService</c>, real
/// <c>FileBankService</c> on a temp directory, real <c>MainWindowViewModel</c>): a withdrawn Pokémon
/// stays in the Bank until the save is actually written with it inside, and every other ending keeps
/// it in the Bank.
/// </summary>
public sealed class BankWithdrawalLifecycleTests : IDisposable
{
    private readonly string _savePath = Path.Combine(Path.GetTempPath(), $"pkhex-bank-{Guid.NewGuid():N}.sav");

    public void Dispose()
    {
        try { File.Delete(_savePath); }
        catch { /* best effort */ }
    }

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

    private static Guid DepositDirect(IBankService bank, PKM pk) =>
        bank.Add(BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "test")).Entry!.Id;

    private (HeadlessAppFixture App, SaveFile Save, IBankService Bank) Boot(GameVersion version = GameVersion.SL)
    {
        var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(version);
        app.LoadSaveInstance(save, _savePath);
        return (app, save, app.Services.GetRequiredService<IBankService>());
    }

    private static string L(string key) => LocalizedStrings.Instance[key];

    [AvaloniaFact]
    public async Task Withdraw_HoldsUntilSaved_ThenTheWrittenSaveRemovesItFromTheBank()
    {
        // Blank SV saves carry untyped blocks that can't be parsed back from disk; B2W2 round-trips cleanly.
        var (app, save, bank) = Boot(GameVersion.B2);
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 25, "Sparky"));

        await app.ViewModel.WithdrawAsync(id);

        Assert.Empty(app.Dialogs.Errors);
        Assert.Equal(25, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.True(save.State.Edited);
        var held = bank.Find(id);
        Assert.NotNull(held?.Pending);
        Assert.NotNull(bank.GetRawData(id)); // the blob is untouched while only memory has the Pokémon

        var bankVm = app.ViewModel.Bank!;
        bankVm.RefreshCurrentBox();
        var slot = bankVm.Slots.Single(s => s.Id == id);
        Assert.True(slot.IsPending);
        Assert.False(slot.CanWithdraw);

        // A second withdrawal of the same entry is refused and writes nothing.
        await app.ViewModel.WithdrawAsync(id);
        Assert.Single(app.Dialogs.Errors);
        Assert.Equal(0, save.GetBoxSlotAtIndex(0, 1).Species);

        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();

        Assert.Null(bank.Find(id));
        Assert.Null(bank.GetRawData(id));
        var onDisk = Assert.IsAssignableFrom<SaveFile>(FileUtil.GetSupportedFile(_savePath));
        Assert.Equal(25, onDisk.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal("Sparky", onDisk.GetBoxSlotAtIndex(0, 0).Nickname);
    }

    [AvaloniaFact]
    public async Task ClosingWithoutSaving_ReleasesTheHold_AndThePokemonStaysInTheBank()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 133, "Eevee"));
        await app.ViewModel.WithdrawAsync(id);
        Assert.NotNull(bank.Find(id)!.Pending);

        app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
        app.Pump();

        Assert.Null(app.Save);
        Assert.Null(bank.Find(id)!.Pending);
        Assert.NotNull(bank.GetRawData(id));
        Assert.False(File.Exists(_savePath)); // nothing was ever written
    }

    [AvaloniaFact]
    public async Task SwitchingToAnotherSave_ReleasesTheHold()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 4, "Ember"));
        await app.ViewModel.WithdrawAsync(id);

        app.LoadSaveInstance(BlankSaveFile.Get(GameVersion.SL));

        Assert.Null(bank.Find(id)!.Pending);
        Assert.Empty(app.ViewModel.BankHolds!.HeldEntryIds);
    }

    [AvaloniaFact]
    public async Task RestartingWhilePending_ReleasesTheHold_AndKeepsTheData()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 7, "Shell"));
        await app.ViewModel.WithdrawAsync(id);
        Assert.NotNull(bank.Find(id)!.Pending);

        // The next app run: a fresh Bank service over the same directory, with no tracker state.
        var nextRun = new FileBankService(app.Services.GetRequiredService<IAppPaths>());

        var entry = nextRun.Find(id);
        Assert.NotNull(entry);
        Assert.Null(entry!.Pending);
        Assert.NotNull(nextRun.GetRawData(id));
    }

    [AvaloniaFact]
    public async Task ClearingTheSlotBeforeSaving_KeepsThePokemonInTheBank()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 1, "Bulby"));
        await app.ViewModel.WithdrawAsync(id);

        save.SetBoxSlotAtIndex(save.BlankPKM, 0, 0);
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();

        var entry = bank.Find(id);
        Assert.NotNull(entry);
        Assert.Null(entry!.Pending);
        Assert.NotNull(bank.GetRawData(id));
        Assert.Contains(app.Dialogs.Infos, i => i.Title == L("Bank_WithdrawKeptTitle") && i.Message.Contains("Bulby", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task EditingTheWithdrawnPokemonBeforeSaving_KeepsTheBankCopy()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 39, "Puff"));
        await app.ViewModel.WithdrawAsync(id);

        var edited = save.GetBoxSlotAtIndex(0, 0);
        edited.Nickname = "Changed";
        edited.RefreshChecksum();
        save.SetBoxSlotAtIndex(edited, 0, 0);
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();

        Assert.NotNull(bank.Find(id)); // worst case is a duplicate, never a loss
    }

    [AvaloniaFact]
    public async Task MovingTheWithdrawnPokemonElsewhereBeforeSaving_StillCompletes()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 150, "Mew2"));
        await app.ViewModel.WithdrawAsync(id);

        var moved = save.GetBoxSlotAtIndex(0, 0);
        save.SetBoxSlotAtIndex(moved, 2, 7);
        save.SetBoxSlotAtIndex(save.BlankPKM, 0, 0);
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();

        Assert.Null(bank.Find(id));
    }

    [AvaloniaFact]
    public async Task UndoingTheWithdrawal_KeepsItInTheBank_AndAllowsWithdrawingAgain()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 92, "Ghost"));
        await app.ViewModel.WithdrawAsync(id);
        Assert.Equal(92, save.GetBoxSlotAtIndex(0, 0).Species);

        app.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(0, save.GetBoxSlotAtIndex(0, 0).Species);

        // Saving now must not remove it: the written file does not contain it.
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();
        Assert.NotNull(bank.Find(id));
        Assert.Null(bank.Find(id)!.Pending);

        // And it can be withdrawn again, this time completing on save.
        await app.ViewModel.WithdrawAsync(id);
        Assert.Equal(92, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();
        Assert.Null(bank.Find(id));
    }

    [AvaloniaFact]
    public async Task UndoWithoutSaving_ThenWithdrawAgain_ReleasesTheStaleHold()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        var id = DepositDirect(bank, Mon(save, 94, "Gengar"));
        await app.ViewModel.WithdrawAsync(id);
        app.ViewModel.UndoCommand.Execute(null);

        await app.ViewModel.WithdrawAsync(id);

        Assert.Empty(app.Dialogs.Errors);
        Assert.Equal(94, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Single(app.ViewModel.BankHolds!.HeldEntryIds);
        Assert.NotNull(bank.Find(id)!.Pending);
    }

    [AvaloniaFact]
    public async Task Withdraw_TargetsTheBoxTheUserIsViewing()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        app.BoxViewer!.NextBoxCommand.Execute(null);
        app.BoxViewer.NextBoxCommand.Execute(null);
        save.SetBoxSlotAtIndex(Mon(save, 10, "Occupy"), 2, 0);
        app.BoxViewer.RefreshCurrentBox();
        var id = DepositDirect(bank, Mon(save, 25, "Pika"));

        Assert.Contains("3", app.ViewModel.WithdrawTargetDescription);
        await app.ViewModel.WithdrawAsync(id);

        Assert.Equal(25, save.GetBoxSlotAtIndex(2, 1).Species);
        Assert.Equal(0, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(2, bank.Find(id)!.Pending!.Box);
    }

    [AvaloniaFact]
    public async Task Withdraw_IntoAnIncompatibleSave_ExplainsWhy_AndHoldsNothing()
    {
        using var app = new HeadlessAppFixture();
        var emerald = BlankSaveFile.Get(GameVersion.E);
        app.LoadSaveInstance(emerald);
        var bank = app.Services.GetRequiredService<IBankService>();
        var id = DepositDirect(bank, Mon(new SAV9SV(), 906, "Sprig"));

        await app.ViewModel.WithdrawAsync(id);

        var error = Assert.Single(app.Dialogs.Errors);
        Assert.Equal(L("Bank_WithdrawFailedTitle"), error.Title);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Null(bank.Find(id)!.Pending);
        Assert.Equal(0, emerald.GetBoxSlotAtIndex(0, 0).Species);
    }

    [AvaloniaFact]
    public async Task Withdraw_WithoutAnOpenSave_IsRefused()
    {
        using var app = new HeadlessAppFixture();
        var bank = app.Services.GetRequiredService<IBankService>();
        var id = DepositDirect(bank, Mon(new SAV9SV(), 25, "Pika"));

        Assert.False(app.ViewModel.CanReceiveWithdrawal);
        await app.ViewModel.WithdrawAsync(id);

        Assert.Equal(L("Bank_NoActiveSave"), Assert.Single(app.Dialogs.Errors).Message);
        Assert.Null(bank.Find(id)!.Pending);
    }

    [AvaloniaFact]
    public async Task SendToBank_FromABoxSlot_ClearsItAndMarksTheSaveEdited()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetBoxSlotAtIndex(Mon(save, 143, "Snorlax"), 0, 4);
        save.State.Edited = false;
        app.BoxViewer!.RefreshCurrentBox();
        Assert.True(app.BoxViewer.CanSendToBank);

        app.BoxViewer.SendToBankCommand.Execute(app.BoxViewer.Slots[4]);
        app.PumpUntil(() => bank.GetAll().Count == 1, because: "deposit to reach the Bank");
        app.Pump();

        Assert.Equal(0, save.GetBoxSlotAtIndex(0, 4).Species);
        Assert.True(save.State.Edited);
        var entry = Assert.Single(bank.GetAll());
        Assert.Equal(143, entry.Info.Species);
        Assert.Equal("Snorlax", entry.Info.Nickname);
        Assert.Equal("pk9", entry.Info.Format);
        Assert.Empty(app.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task SendToBank_FromTheParty_CompactsIt_AndNeverEmptiesIt()
    {
        var (app, save, bank) = Boot();
        using var _ = app;
        save.SetPartySlotAtIndex(Mon(save, 1, "First"), 0);
        save.SetPartySlotAtIndex(Mon(save, 4, "Second"), 1);
        app.ViewModel.PartyViewer!.RefreshParty();

        await app.ViewModel.SendToBankAsync(SlotLocation.FromParty(0));

        Assert.Equal(1, save.PartyCount);
        Assert.Equal(4, save.GetPartySlotAtIndex(0).Species);
        Assert.Single(bank.GetAll());

        await app.ViewModel.SendToBankAsync(SlotLocation.FromParty(0));

        Assert.Equal(L("Bank_DepositLastPartyMember"), Assert.Single(app.Dialogs.Errors).Message);
        Assert.Equal(1, save.PartyCount);
        Assert.Single(bank.GetAll()); // nothing was added for the refused deposit
    }

    [AvaloniaFact]
    public async Task SendToBank_IntoAReadOnlyBank_LeavesTheSlotAlone()
    {
        var paths = new FakeAppPaths();
        var bankRoot = Path.Combine(paths.DataDirectory, "bank");
        Directory.CreateDirectory(bankRoot);
        File.WriteAllText(Path.Combine(bankRoot, "index.json"), "{corrupt");
        File.WriteAllText(Path.Combine(bankRoot, "index.json.bak"), "{corrupt");
        using var app = new HeadlessAppFixture(svc => svc.AddSingleton<IBankService>(new FileBankService(paths)));
        var save = BlankSaveFile.Get(GameVersion.SL);
        app.LoadSaveInstance(save);
        save.SetBoxSlotAtIndex(Mon(save, 25, "Pika"), 0, 0);

        await app.ViewModel.SendToBankAsync(SlotLocation.FromBox(0, 0));

        Assert.Equal(25, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(L("Bank_Error_ReadOnly"), Assert.Single(app.Dialogs.Errors).Message);
        Assert.True(app.ViewModel.Bank!.IsReadOnly);
    }

    [AvaloniaFact]
    public void OpenBank_UsesAPersistentWindow_ThatSurvivesSaveChanges()
    {
        using var app = new HeadlessAppFixture();
        Assert.True(app.ViewModel.OpenBankCommand.CanExecute(null)); // no save needed

        app.ViewModel.OpenBankCommand.Execute(null);
        var bank = app.ViewModel.Bank;
        Assert.Same(bank, Assert.Single(app.Windows.ShownPersistent).ViewModel);
        Assert.Empty(app.Windows.ShownTools);

        var closesBefore = app.Windows.CloseAllToolsCount;
        app.LoadSaveInstance(BlankSaveFile.Get(GameVersion.SL));

        Assert.True(app.Windows.CloseAllToolsCount > closesBefore);
        Assert.Same(bank, app.ViewModel.Bank); // the save change did not rebuild or close the Bank
        Assert.True(app.ViewModel.CanReceiveWithdrawal);
        Assert.Contains(GameInfo.GetVersionName(GameVersion.SL), bank!.TargetText);
    }
}
