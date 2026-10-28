using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Media;
using PKHeX.Presentation.Localization;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class CosmeticInventory4Tests
{
    private static SAV4 Load(string name) => Assert.IsAssignableFrom<SAV4>(SaveFileFixture.LoadSave(
        Path.Combine(SaveFileFixture.FindSaveFilesPath()!, name)));

    [Theory]
    [InlineData("gen4_heartgold.sav")]
    [InlineData("gen4_platinum.sav")]
    [InlineData("gen4_diamond.sav")]
    public void IndividualFieldsRoundTripWithoutChangingOtherGeneralData(string file)
    {
        var save = Load(file);
        var before = save.General.ToArray();
        var vm = new CosmeticInventory4EditorViewModel(save);
        vm.Categories[0].Entries[0].Value = 37;
        vm.Categories[1].Entries[0].Value = 4;
        vm.Categories[1].Entries[AccessoryInfo.MaxMulti + 1].Value = 1;
        foreach (var row in vm.Categories[2].Entries) row.Value = BackdropInfo.Count;
        vm.Categories[2].Entries[0].Value = 0;
        Assert.Equal(before, save.General.ToArray());
        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);
        Assert.Equal(37, save.GetSealCount((Seal4)0));
        Assert.Equal(4, save.GetAccessoryOwnedCount((Accessory4)0));
        Assert.Equal(1, save.GetAccessoryOwnedCount((Accessory4)(AccessoryInfo.MaxMulti + 1)));
        Assert.Equal(0, save.GetBackdropPosition((Backdrop4)0));
        var reloaded = new CosmeticInventory4EditorViewModel(save);
        Assert.Equal(37, reloaded.Categories[0].Entries[0].Value);
        // Restore just the edited cosmetic records and prove no unrelated General bytes changed.
        var original = Load(file);
        save.SetSealCase(original.GetSealCase());
        for (int i = 0; i < AccessoryInfo.Count; i++)
            save.SetAccessoryOwnedCount((Accessory4)i, original.GetAccessoryOwnedCount((Accessory4)i));
        for (int i = 0; i < BackdropInfo.Count; i++)
            save.SetBackdropPosition((Backdrop4)i, original.GetBackdropPosition((Backdrop4)i));
        Assert.Equal(before, save.General.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BulkActionsHaveWholeCategoryScopeAndRemainDiscardable(int kind)
    {
        var save = Load("gen4_heartgold.sav");
        var before = save.General.ToArray();
        var vm = new CosmeticInventory4EditorViewModel(save);
        vm.SelectedCategory = vm.Categories[kind];
        vm.SearchText = "0";
        vm.ClearCategoryCommand.Execute(null);
        Assert.All(vm.SelectedCategory.Entries, r => Assert.Equal(kind == 2 ? BackdropInfo.Count : 0, r.Value));
        vm.GiveLegalCommand.Execute(null);
        Assert.All(vm.SelectedCategory.Entries.Where(r => r.IsLegal), r => Assert.Equal(kind == 2 ? r.Index : r.Maximum, r.Value));
        Assert.All(vm.SelectedCategory.Entries.Where(r => !r.IsLegal), r => Assert.Equal(kind == 2 ? BackdropInfo.Count : 0, r.Value));
        vm.GiveIncludingUnreleasedCommand.Execute(null);
        Assert.All(vm.SelectedCategory.Entries, r => Assert.Equal(kind == 2 ? r.Index : r.Maximum, r.Value));
        Assert.Equal(before, save.General.ToArray());
        vm.ResetCommand.Execute(null);
        Assert.All(vm.Categories.SelectMany(c => c.Entries), r => Assert.Equal(r.OriginalValue, r.Value));
        vm.ClearCategoryCommand.Execute(null);
        bool closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, save.General.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnreleasedPresetAndClearPersist(int kind)
    {
        var save = Load("gen4_heartgold.sav");
        var vm = new CosmeticInventory4EditorViewModel(save);
        vm.SelectedCategory = vm.Categories[kind];
        vm.GiveIncludingUnreleasedCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        var loaded = new CosmeticInventory4EditorViewModel(save);
        Assert.All(loaded.Categories[kind].Entries, r => Assert.Equal(kind == 2 ? r.Index : r.Maximum, r.Value));
        loaded.SelectedCategory = loaded.Categories[kind];
        loaded.ClearCategoryCommand.Execute(null);
        loaded.SaveCommand.Execute(null);
        var cleared = new CosmeticInventory4EditorViewModel(save);
        Assert.All(cleared.Categories[kind].Entries, r => Assert.Equal(kind == 2 ? BackdropInfo.Count : 0, r.Value));
    }

    [Fact]
    public void InvalidNewValuesAndDuplicatePositionsBlockCommit()
    {
        var save = Load("gen4_heartgold.sav");
        var before = save.General.ToArray();
        var vm = new CosmeticInventory4EditorViewModel(save);
        vm.Categories[0].Entries[0].Value = 100;
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.NotEmpty(vm.ValidationText);
        vm.ResetCommand.Execute(null);
        vm.SelectedCategory = vm.Categories[2];
        vm.GiveIncludingUnreleasedCommand.Execute(null);
        vm.Categories[2].Entries[1].Value = 0;
        Assert.False(vm.CanSave);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.General.ToArray());
    }

    [Fact]
    public void NoOpPreservesUnknownStoredValuesAndEditedState()
    {
        var save = Load("gen4_heartgold.sav");
        save.GetSealCase()[0] = 255;
        save.General[0] ^= 1;
        save.State.Edited = false;
        var before = save.General.ToArray();
        var vm = new CosmeticInventory4EditorViewModel(save);
        Assert.Equal(255, vm.Categories[0].Entries[0].Value);
        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.General.ToArray());
        Assert.False(save.State.Edited);
    }

    [AvaloniaFact]
    public void MenuAndLocatorExposeOnlyCompatibleSaveTypes()
    {
        using var app = new HeadlessAppFixture();
        var action = Assert.Single(app.ViewModel.ToolMenuGroups.SelectMany(g => g.Items),
            e => ReferenceEquals(e.Command, app.ViewModel.OpenCosmeticInventory4Command));
        Assert.False(action.IsAvailable);
        app.LoadSaveInstance(new SAV4HGSS());
        Assert.True(action.IsAvailable);
        Assert.IsType<CosmeticInventory4Editor>(ViewLocator.Build(new CosmeticInventory4EditorViewModel(new SAV4HGSS())));
        app.LoadSaveInstance(new SAV4Pt());
        Assert.True(action.IsAvailable);
        app.LoadSaveInstance(new SAV4BR());
        Assert.False(action.IsAvailable);
        app.LoadSaveInstance(new SAV6XY());
        Assert.False(action.IsAvailable);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompactViewHasReachableActionsAndVirtualizedRows(int kind)
    {
        using var app = new HeadlessAppFixture();
        var vm = new CosmeticInventory4EditorViewModel(Load("gen4_heartgold.sav"));
        vm.SelectedCategory = vm.Categories[kind];
        app.Window.MinWidth = 0;
        app.Window.MinHeight = 0;
        app.Window.Width = 620;
        app.Window.Height = 620;
        app.Window.Content = new CosmeticInventory4Editor { DataContext = vm };
        app.Pump();
        var grid = app.Window.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.True(grid.Bounds.Height > 100);
        var save = app.Window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveCommand));
        Assert.True(save.IsEffectivelyVisible);
        Assert.True(save.Bounds.Height > 0);
        Assert.NotEmpty(vm.VisibleEntries);
        vm.SearchText = vm.SelectedCategory.Entries[0].Name;
        app.Pump();
        Assert.Contains(vm.SelectedCategory.Entries[0], vm.VisibleEntries);
        vm.SearchText = string.Empty;
        app.Pump();
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
        {
            var directory = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!;
            Directory.CreateDirectory(directory);
            Assert.NotNull(app.CaptureFrame(Path.Combine(directory, $"gen4-cosmetic-{kind}.png")));
        }
    }
    [Fact]
    public void LegalBackdropPresetRetainsOwnedUnreleasedEntryWithUniquePosition()
    {
        var save = Load("gen4_heartgold.sav");
        for (int i = 0; i < BackdropInfo.Count; i++) save.RemoveBackdrop((Backdrop4)i);
        save.SetBackdropPosition(Backdrop4.Theater, 0);
        var vm = new CosmeticInventory4EditorViewModel(save);
        vm.SelectedCategory = vm.Categories[2];
        vm.GiveLegalCommand.Execute(null);
        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);
        for (int i = 0; i < BackdropInfo.Count; i++) Assert.Equal(i, save.GetBackdropPosition((Backdrop4)i));
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void LongLocalizedLabelsAndActionsFitScaledMinimumViewport(double scale)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        try
        {
            using var app = new HeadlessAppFixture();
            app.ViewModel.LanguageService.SetLanguage("de");
            var vm = new CosmeticInventory4EditorViewModel(Load("gen4_heartgold.sav"));
            app.Window.MinWidth = 0;
            app.Window.MinHeight = 0;
            app.Window.Width = 1024;
            app.Window.Height = 720;
            var view = new CosmeticInventory4Editor { DataContext = vm };
            app.Window.Content = new LayoutTransformControl { Child = view, LayoutTransform = new ScaleTransform(scale, scale) };
            app.Pump();
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.True(grid.Bounds.Height > 80);
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b =>
                ReferenceEquals(b.Command, vm.SaveCommand) || ReferenceEquals(b.Command, vm.CancelCommand)
                || ReferenceEquals(b.Command, vm.ResetCommand)))
            {
                var point = button.TranslatePoint(new global::Avalonia.Point(0, button.Bounds.Height), app.Window);
                Assert.NotNull(point);
                Assert.InRange(point.Value.Y, 1, 720);
            }
            if (scale == 1.5 && Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
                Assert.NotNull(app.CaptureFrame(Path.Combine(Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!, "gen4-cosmetic-de-150.png")));
        }
        finally { new PKHeX.Application.Services.LanguageService().SetLanguage(previous); LocalizedStrings.Instance.SetLanguage(previous); }
    }

}
