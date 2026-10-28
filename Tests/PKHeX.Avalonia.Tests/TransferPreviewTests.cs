using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Application.UseCases;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Transfer preview: before a Pokémon from another format is written into the save (Bank withdraw,
/// dropped file, slot dragged from another window), the user sees what the conversion changes and
/// whether the result is legal, and nothing is written unless they confirm.
/// </summary>
public sealed class TransferPreviewTests
{
    private const ushort Pikachu = 25;

    private static string L(string key) => LocalizedStrings.Instance[key];

    /// <summary>A Gen 3 Pikachu caught on Route 4 in Emerald, ready for Pal Park.</summary>
    private static PKM Gen3Pikachu()
    {
        var pk = new PK3
        {
            Species = Pikachu,
            CurrentLevel = 20,
            Version = GameVersion.E,
            Language = (int)LanguageID.English,
            OriginalTrainerName = "MAY",
            TID16 = 12345,
            SID16 = 54321,
            MetLocation = 20,
            MetLevel = 5,
            Ball = (byte)Ball.Poke,
            Move1 = 84, // Thunder Shock
        };
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(Pikachu, pk.Language, 3);
        pk.SetUnshiny();
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void Preview_Gen3ToGen4_ListsTheConversion_AndLeavesTheSaveUnchanged()
    {
        var sav = BlankSaveFile.Get(GameVersion.Pt);
        var before = sav.Data.ToArray(); // a blank Platinum save cannot be serialized, so compare its buffer
        var source = Gen3Pikachu();
        var conversion = BankEntityConversion.ConvertForSave(sav, source);
        Assert.True(conversion.Success, string.Join(" | ", conversion.Details));

        var preview = new EntityTransferPreviewUseCase().Build(sav, source, conversion.Entity!);

        Assert.True(preview.FormatChanged);
        Assert.True(preview.IsNotable);
        Assert.IsType<PK4>(preview.Result);
        var format = Assert.Single(preview.Changes, c => c.Field == EntityTransferField.Format);
        Assert.Equal(("PK3", "PK4"), (format.Before, format.After));
        var met = Assert.Single(preview.Changes, c => c.Field == EntityTransferField.MetLocation);
        Assert.Equal(GameInfo.GetLocationName(false, preview.Result.MetLocation, 4, 4, GameVersion.Pt), met.After); // Pal Park, by name
        Assert.DoesNotContain(preview.Changes, c => c.Field is EntityTransferField.Species or EntityTransferField.TrainerId);
        Assert.False(string.IsNullOrWhiteSpace(preview.LegalityReport));
        Assert.Equal(before, sav.Data.ToArray());
        Assert.NotSame(conversion.Entity, preview.Result); // the caller's entity is not adapted in place
    }

    [Fact]
    public void Preview_SameFormat_NoChange_IsNotWorthShowing()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        var pk = sav.BlankPKM;
        pk.Species = Pikachu;
        pk.CurrentLevel = 5;
        pk.SetUnshiny();
        pk.RefreshChecksum();

        var preview = new EntityTransferPreviewUseCase().Build(sav, pk, pk.Clone());

        Assert.False(preview.FormatChanged);
        Assert.Empty(preview.Changes);
        Assert.False(preview.LosesLegality); // illegal before and after: the transfer itself changes nothing
        Assert.False(preview.IsNotable);
    }

    [Fact]
    public void Preview_ShowsTrainerIdTheWayTheTargetDoes_SoAnUnchangedIdIsNotAChange()
    {
        var x = BlankSaveFile.Get(GameVersion.X);
        var pk6 = x.BlankPKM;
        pk6.Species = Pikachu;
        pk6.CurrentLevel = 10;
        pk6.TID16 = 12345;
        pk6.SID16 = 54321;
        pk6.SetUnshiny();
        pk6.RefreshChecksum();
        var sun = BlankSaveFile.Get(GameVersion.SN);

        var preview = new EntityTransferPreviewUseCase().Build(sun, pk6, BankEntityConversion.ConvertForSave(sun, pk6).Entity!);

        Assert.True(preview.FormatChanged);
        Assert.DoesNotContain(preview.Changes, c => c.Field == EntityTransferField.TrainerId);
    }

    [Fact]
    public void Preview_LosingLegality_IsAlwaysWorthShowing()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        var pk = sav.BlankPKM;
        var preview = new EntityTransferPreview(pk, pk, [], SourceLegal: true, ResultLegal: false, "report");

        Assert.True(preview.LosesLegality);
        Assert.True(preview.IsNotable);
    }

    [Fact]
    public void ViewModel_LocalizesFieldsAndFlags_AndReportsTheChoice()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        var pk = sav.BlankPKM;
        pk.Species = Pikachu;
        var shiny = new EntityTransferChange(EntityTransferField.Shiny, bool.FalseString, bool.TrueString);
        var item = new EntityTransferChange(EntityTransferField.HeldItem, "Leftovers", string.Empty);
        var previews = new[]
        {
            new EntityTransferPreview(pk, pk, [shiny, item], SourceLegal: true, ResultLegal: false, "report"),
            new EntityTransferPreview(pk, pk, [], SourceLegal: true, ResultLegal: true, "report"),
        };

        var vm = new TransferPreviewViewModel(previews, "Violet (ANN)", L("TransferPreview_Withdraw"));

        Assert.True(vm.ShowList);
        Assert.True(vm.HasWarning);
        Assert.Same(vm.Items[0], vm.Selected);
        var rows = vm.Items[0].Changes;
        Assert.Equal((L("TransferPreview_Field_Shiny"), L("TransferPreview_No"), L("TransferPreview_Yes")), (rows[0].Field, rows[0].Before, rows[0].After));
        Assert.Equal((L("TransferPreview_Field_HeldItem"), "Leftovers", L("TransferPreview_None")), (rows[1].Field, rows[1].Before, rows[1].After));
        Assert.Equal(L("TransferPreview_LosesLegality"), vm.Items[0].LegalityText);
        Assert.False(vm.Items[1].HasChanges);

        var closed = 0;
        vm.CloseRequested = () => closed++;
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.Confirmed);
        vm.CancelCommand.Execute(null);
        Assert.False(vm.Confirmed);
        Assert.Equal(2, closed);
    }

    // --- in the composed app -------------------------------------------------

    private static (HeadlessAppFixture App, SaveFile Save, IBankService Bank, Guid Id) BootWithGen3InBank()
    {
        var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.Pt);
        app.LoadSaveInstance(save);
        var bank = app.Services.GetRequiredService<IBankService>();
        var source = Gen3Pikachu();
        var id = bank.Add(BankEntryInfoFactory.GetRawData(source), BankEntryInfoFactory.CreateInfo(source, "test")).Entry!.Id;
        return (app, save, bank, id);
    }

    [AvaloniaFact]
    public async Task Withdraw_FromAnotherFormat_PreviewsFirst_AndCancelWritesNothing()
    {
        var (app, save, bank, id) = BootWithGen3InBank();
        using var _ = app;
        app.Dialogs.ConfirmResult = false;

        await app.ViewModel.WithdrawAsync(id);

        var preview = Assert.IsType<TransferPreviewViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);
        Assert.Equal(L("TransferPreview_Title"), app.Windows.ShownDialogs[0].Title);
        Assert.Contains(preview.Items[0].Changes, r => r.Field == L("TransferPreview_Field_Format"));
        Assert.Equal(0, save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Null(bank.Find(id)!.Pending);
        Assert.False(save.State.Edited);
        Assert.Empty(app.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Withdraw_FromAnotherFormat_ConfirmedPreview_WritesTheConvertedPokemon()
    {
        var (app, save, bank, id) = BootWithGen3InBank();
        using var _ = app;
        app.Dialogs.ConfirmResult = true;

        await app.ViewModel.WithdrawAsync(id);

        Assert.Single(app.Windows.ShownDialogs);
        var written = save.GetBoxSlotAtIndex(0, 0);
        Assert.IsType<PK4>(written);
        Assert.Equal(Pikachu, written.Species);
        Assert.NotNull(bank.Find(id)!.Pending);
    }

    [AvaloniaFact]
    public async Task Withdraw_WithPreviewsOff_WritesWithoutAsking()
    {
        var (app, save, _, id) = BootWithGen3InBank();
        using var __ = app;
        app.Services.GetRequiredService<AppSettings>().EditorBehavior.PreviewTransfers = false;

        await app.ViewModel.WithdrawAsync(id);

        Assert.Empty(app.Windows.ShownDialogs);
        Assert.Equal(Pikachu, save.GetBoxSlotAtIndex(0, 0).Species);
    }

    [AvaloniaFact]
    public async Task WithdrawMarked_ThePreviewIsTheConfirmation()
    {
        var (app, save, bank, first) = BootWithGen3InBank();
        using var _ = app;
        var second = Gen3Pikachu();
        second.Nickname = "SPARK";
        second.IsNicknamed = true;
        second.RefreshChecksum();
        var id2 = bank.Add(BankEntryInfoFactory.GetRawData(second), BankEntryInfoFactory.CreateInfo(second, "test")).Entry!.Id;
        app.Dialogs.ConfirmResult = true;

        var withdrawn = await app.ViewModel.WithdrawManyAsync([first, id2]);

        var preview = Assert.IsType<TransferPreviewViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);
        Assert.Equal(2, preview.Items.Count);
        Assert.True(preview.ShowList);
        Assert.Empty(app.Dialogs.Confirmations); // no second, plain confirmation
        Assert.Equal(2, withdrawn.Count);
        Assert.Equal(Pikachu, save.GetBoxSlotAtIndex(0, 1).Species);
    }

    [AvaloniaFact]
    public async Task DroppedFile_FromAnotherFormat_PreviewsFirst()
    {
        using var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.Pt);
        app.LoadSaveInstance(save);
        var path = Path.Combine(Path.GetTempPath(), $"pkhex-transfer-{Guid.NewGuid():N}.pk3");
        File.WriteAllBytes(path, Gen3Pikachu().Data.ToArray());
        try
        {
            app.Dialogs.ConfirmResult = false;
            await app.BoxViewer!.HandleFileDropAsync([path], 3);
            Assert.IsType<TransferPreviewViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);
            Assert.Equal(0, save.GetBoxSlotAtIndex(0, 3).Species);

            app.Dialogs.ConfirmResult = true;
            await app.BoxViewer!.HandleFileDropAsync([path], 3);
            app.Pump();
            Assert.Equal(2, app.Windows.ShownDialogs.Count);
            Assert.IsType<PK4>(save.GetBoxSlotAtIndex(0, 3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task DroppedFile_SameFormat_WritesWithoutAPreview()
    {
        using var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.Pt);
        app.LoadSaveInstance(save);
        var pk4 = (PK4)BankEntityConversion.ConvertForSave(save, Gen3Pikachu()).Entity!;
        save.AdaptToSaveFile(pk4, isParty: false); // exactly as the save would store it
        var path = Path.Combine(Path.GetTempPath(), $"pkhex-transfer-{Guid.NewGuid():N}.pk4");
        File.WriteAllBytes(path, pk4.Data.ToArray());
        try
        {
            await app.BoxViewer!.HandleFileDropAsync([path], 0);
            app.Pump();
            Assert.Empty(app.Windows.ShownDialogs);
            Assert.Equal(Pikachu, save.GetBoxSlotAtIndex(0, 0).Species);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
