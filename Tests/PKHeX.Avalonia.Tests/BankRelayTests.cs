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
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Organizer Phase 1b, the Bank relay: marked Pokémon move between two open saves (two app
/// instances) by "Send marked to Bank" in one and "Withdraw marked" in the other's Bank window.
/// Covers the bulk withdrawal itself (fill forward from the viewed box, one undo, skip reasons,
/// cancel) and the full two-instance relay through real files on disk.
/// </summary>
public sealed class BankRelayTests : IDisposable
{
    private readonly FakeAppPaths _sharedPaths = new();
    private readonly string _pathA = Path.Combine(Path.GetTempPath(), $"pkhex-relay-a-{Guid.NewGuid():N}.sav");
    private readonly string _pathB = Path.Combine(Path.GetTempPath(), $"pkhex-relay-b-{Guid.NewGuid():N}.sav");

    public void Dispose()
    {
        foreach (var path in new[] { _pathA, _pathB })
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }
        try { Directory.Delete(Path.GetDirectoryName(_sharedPaths.DataDirectory)!, recursive: true); }
        catch { /* best effort */ }
    }

    private static string L(string key) => LocalizedStrings.Instance[key];
    private static string F(string key, params object[] args) => LocalizedStrings.Instance.Format(key, args);

    /// <summary>One app instance; every instance in a test shares one Bank directory.</summary>
    private HeadlessAppFixture Instance(string savePath)
    {
        var app = new HeadlessAppFixture(svc => svc.AddSingleton<IAppPaths>(_sharedPaths));
        // B2W2 rather than SV: blank SV saves carry untyped blocks that can't be re-read from disk.
        app.LoadSaveInstance(BlankSaveFile.Get(GameVersion.B2), savePath);
        app.Dialogs.ConfirmResult = true;
        return app;
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

    private static Guid Deposit(IBankService bank, PKM pk) =>
        bank.Add(BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "test")).Entry!.Id;

    private static BankViewModel OpenBank(HeadlessAppFixture app)
    {
        app.ViewModel.OpenBankCommand.Execute(null);
        return app.ViewModel.Bank!;
    }

    [AvaloniaFact]
    public async Task MarkedPokemon_MoveBetweenTwoOpenSaves_ThroughTheBank()
    {
        using var a = Instance(_pathA);
        var saveA = a.Save!;
        saveA.SetBoxSlotAtIndex(Mon(saveA, 25, "Sparky"), 0, 0);
        saveA.SetBoxSlotAtIndex(Mon(saveA, 133, "Eevee"), 0, 3);
        saveA.SetBoxSlotAtIndex(Mon(saveA, 4, "Ember"), 0, 7);
        a.BoxViewer!.RefreshCurrentBox();

        // Instance A: mark three and send them to the Bank, then save.
        a.BoxViewer.ToggleSelectionModeCommand.Execute(null);
        a.BoxViewer.SelectAllInBoxCommand.Execute(null);
        await a.BoxViewer.BulkSendToBankCommand.ExecuteAsync(null);
        Assert.True(await a.Gateway.SaveFileAsync());
        a.Pump();

        using var b = Instance(_pathB);
        var bank = b.Services.GetRequiredService<IBankService>();
        Assert.Equal(3, bank.GetAll().Count);
        Assert.All(bank.GetAll(), e => Assert.Null(e.Pending)); // A's save confirmed the deposits

        // Instance B: mark them in the Bank window and withdraw together, then save.
        var bankVm = OpenBank(b);
        bankVm.IsSelectionModeEnabled = true;
        bankVm.MarkAllInBoxCommand.Execute(null);
        Assert.Equal(3, bankVm.MarkedCount);
        await bankVm.WithdrawMarkedCommand.ExecuteAsync(null);
        Assert.Equal(0, bankVm.MarkedCount);
        Assert.True(await b.Gateway.SaveFileAsync());
        b.Pump();

        Assert.Empty(bank.GetAll());
        var fileA = Assert.IsAssignableFrom<SaveFile>(FileUtil.GetSupportedFile(_pathA));
        var fileB = Assert.IsAssignableFrom<SaveFile>(FileUtil.GetSupportedFile(_pathB));
        Assert.All(fileA.GetBoxData(0), pk => Assert.Equal(0, pk.Species));
        Assert.Equal(new ushort[] { 25, 133, 4 }, fileB.GetBoxData(0).Take(3).Select(pk => pk.Species).ToArray());
        Assert.Equal("Eevee", fileB.GetBoxSlotAtIndex(0, 1).Nickname);
        Assert.Empty(a.Dialogs.Errors);
        Assert.Empty(b.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task WithdrawMany_FillsForwardFromTheViewedBox_AndOneUndoRevertsIt()
    {
        using var app = Instance(_pathB);
        var sav = app.Save!;
        for (var slot = 0; slot < sav.BoxSlotCount - 1; slot++)
            sav.SetBoxSlotAtIndex(Mon(sav, 1, "Filler"), 0, slot);
        var bank = app.Services.GetRequiredService<IBankService>();
        var ids = new[] { Deposit(bank, Mon(sav, 25, "A")), Deposit(bank, Mon(sav, 26, "B")), Deposit(bank, Mon(sav, 27, "C")) };

        var withdrawn = await app.ViewModel.WithdrawManyAsync(ids);

        Assert.Equal(ids, withdrawn);
        Assert.Equal(25, sav.GetBoxSlotAtIndex(0, sav.BoxSlotCount - 1).Species);
        Assert.Equal(26, sav.GetBoxSlotAtIndex(1, 0).Species);
        Assert.Equal(27, sav.GetBoxSlotAtIndex(1, 1).Species);
        Assert.All(ids, id => Assert.True(bank.Find(id)!.IsPendingWithdrawal));
        Assert.Contains(F("Bank_WithdrawMarkedResult", 3), app.Dialogs.Infos.Last().Message);

        app.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(0, sav.GetBoxSlotAtIndex(1, 0).Species);
        Assert.Equal(0, sav.GetBoxSlotAtIndex(0, sav.BoxSlotCount - 1).Species);

        // Saving after the undo finishes nothing: the Pokémon stay in the Bank.
        Assert.True(await app.Gateway.SaveFileAsync());
        app.Pump();
        Assert.All(ids, id => Assert.Null(bank.Find(id)!.Pending));
    }

    [AvaloniaFact]
    public async Task WithdrawMany_SkipsAndReportsWhatItCannotPlace()
    {
        using var app = Instance(_pathB);
        var sav = app.Save!;
        var bank = app.Services.GetRequiredService<IBankService>();
        var ok = Deposit(bank, Mon(sav, 25, "Fine"));
        var tooNew = Deposit(bank, new PK9 { Species = 1000 }); // Gholdengo has no route into Gen 5
        var held = Deposit(bank, Mon(sav, 7, "Held"));
        bank.MarkPendingWithdrawal(held, new BankPendingWithdrawal(Guid.NewGuid(), "Elsewhere", null, 0, 0, DateTimeOffset.UtcNow));

        var withdrawn = await app.ViewModel.WithdrawManyAsync([ok, tooNew, held]);

        Assert.Equal([ok], withdrawn);
        Assert.Equal(25, sav.GetBoxSlotAtIndex(0, 0).Species);
        var summary = app.Dialogs.Infos.Last().Message;
        Assert.Contains(F("Bank_SkippedIncompatible", 1), summary);
        Assert.Contains(F("Bank_SkippedUnavailable", 1), summary);
        // One confirmation, for the one it can place: with transfer previews on (the default) the preview is that confirmation.
        var preview = Assert.IsType<PKHeX.Presentation.ViewModels.TransferPreviewViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);
        Assert.Single(preview.Items);
        Assert.Empty(app.Dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task WithdrawMany_Cancel_ChangesNothing()
    {
        using var app = Instance(_pathB);
        var sav = app.Save!;
        var bank = app.Services.GetRequiredService<IBankService>();
        var id = Deposit(bank, Mon(sav, 25, "Sparky"));
        app.Dialogs.ConfirmResult = false;

        var withdrawn = await app.ViewModel.WithdrawManyAsync([id]);

        Assert.Empty(withdrawn);
        Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Null(bank.Find(id)!.Pending);
        Assert.False(sav.State.Edited);
    }

    [AvaloniaFact]
    public void BankMarks_SurviveBoxPaging_AndTurningSelectionOffClearsThem()
    {
        using var app = Instance(_pathB);
        var bank = app.Services.GetRequiredService<IBankService>();
        var sav = app.Save!;
        var first = Deposit(bank, Mon(sav, 25, "One"));
        bank.Add(BankEntryInfoFactory.GetRawData(Mon(sav, 26, "Two")), BankEntryInfoFactory.CreateInfo(Mon(sav, 26, "Two"), "test"), box: 1, slot: 0);
        var vm = OpenBank(app);

        vm.IsSelectionModeEnabled = true;
        vm.SelectSlotByClickCommand.Execute(vm.Slots[0]);
        Assert.True(vm.Slots[0].IsMarked);
        vm.NextBoxCommand.Execute(null);
        vm.MarkAllInBoxCommand.Execute(null);
        vm.PreviousBoxCommand.Execute(null);

        Assert.True(vm.Slots[0].IsMarked);
        Assert.Equal(2, vm.MarkedCount);
        Assert.Equal(first, vm.MarkedIds[0]); // Bank order: box 1 before box 2
        Assert.Contains(L("Organizer_MarkedOverlay"), vm.Slots[0].AccessibleName);

        vm.SelectSlotByClickCommand.Execute(vm.Slots[0]); // clicking again unmarks
        Assert.Equal(1, vm.MarkedCount);
        vm.IsSelectionModeEnabled = false;
        Assert.Equal(0, vm.MarkedCount);
        Assert.False(vm.WithdrawMarkedCommand.CanExecute(null));
    }
}
