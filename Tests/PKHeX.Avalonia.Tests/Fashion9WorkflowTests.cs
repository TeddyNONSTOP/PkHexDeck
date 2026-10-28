using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Fashion9WorkflowTests
{
    private static SaveFile Load(bool za)
    {
        var path = Path.Combine(SaveFileFixture.FindSaveFilesPath()!, za ? "gen9a_legendsza.main" : "gen9_scarlet.main");
        return Assert.IsAssignableFrom<SaveFile>(SaveFileFixture.LoadSave(path));
    }
    private static void Empty(SaveFile save)
    {
        using var vm = new Fashion9EditorViewModel(save);
        foreach (var category in vm.Categories)
        for (int offset = 0; offset + 8 <= category.Source.Data.Length; offset += 8)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(category.Source.Data[offset..], FashionItem9.None);
            BinaryPrimitives.WriteUInt32LittleEndian(category.Source.Data[(offset + 4)..], 0);
        }
    }
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public async Task SvBaseUnlockIsGenderAwareStagedAndMatchesCore(byte gender)
    {
        var save = Assert.IsType<SAV9SV>(Load(false));
        save.Gender = gender;
        Empty(save);
        var expected = (SAV9SV)save.Clone();
        var count = PlayerFashionUnlock9.UnlockBase(expected.Accessor, gender);
        using var vm = new Fashion9EditorViewModel(save, new RecordingDialogService { ConfirmResult = true });
        var before = vm.Categories.Select(c => c.Source.Data.ToArray()).ToArray();
        await vm.UnlockBaseCommand.ExecuteAsync(null);
        Assert.True(vm.CanUndo);
        Assert.Equal(count, vm.Categories.Sum(c => c.Items.Count(r => !r.IsEmpty)));
        for (int i = 0; i < vm.Categories.Count; i++) Assert.Equal(before[i], vm.Categories[i].Source.Data.ToArray());
        vm.SaveCommand.Execute(null);
        foreach (var category in vm.Categories)
            Assert.Equal(expected.Accessor.GetBlock(category.Key).Data.ToArray(), category.Source.Data.ToArray());
        Assert.Contains(count.ToString(), vm.StatusText, StringComparison.Ordinal);
        await vm.UnlockBaseCommand.ExecuteAsync(null);
        Assert.Contains("0", vm.StatusText, StringComparison.Ordinal);
        Assert.False(vm.SupportsOwnedAction);
    }
    [Fact]
    public async Task SvUnlockPreservesUnknownRecordsAfterHolesAndRejectsOverflowAtomically()
    {
        var save = Assert.IsType<SAV9SV>(Load(false));
        Empty(save);
        using (var edit = new Fashion9EditorViewModel(save))
        {
            var row = edit.Categories[0].Items[3];
            row.Value = 0xDEADBEEF;
            row.Flags = 0xA0000000;
            edit.SaveCommand.Execute(null);
        }
        using var vm = new Fashion9EditorViewModel(save, new RecordingDialogService { ConfirmResult = true });
        await vm.UnlockBaseCommand.ExecuteAsync(null);
        Assert.Equal(0xDEADBEEFu, vm.Categories[0].Items[3].Value);
        Assert.Equal(0xA0000000u, vm.Categories[0].Items[3].Flags);
        vm.UndoCommand.Execute(null);
        Assert.Equal(0xDEADBEEFu, vm.Categories[0].Items[3].Value);
        foreach (var row in vm.Categories[0].Items.Where(r => r.IsEmpty)) row.Value = (uint)(0x100000 + row.Index);
        var bytes = vm.Categories.Select(c => c.Staged.Data.ToArray()).ToArray();
        await vm.UnlockBaseCommand.ExecuteAsync(null);
        Assert.Equal(LocalizedStrings.Instance["Fashion9Flow_Full"], vm.StatusText);
        for (int i = 0; i < bytes.Length; i++) Assert.Equal(bytes[i], vm.Categories[i].Staged.Data.ToArray());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyUnknownIdsFlagsDuplicatesAndCancelRoundTrip(bool za)
    {
        var save = Load(za);
        Empty(save);
        using var vm = new Fashion9EditorViewModel(save);
        var category = vm.Categories[0];
        var before = category.Source.Data.ToArray();
        Assert.True(category.Items[0].IsEmpty);
        Assert.Equal(LocalizedStrings.Instance["Fashion9Flow_Empty"], category.Items[0].Identity);
        var row = category.Items[0];
        row.Value = 0x12345678;
        row.Flags = 0xA0000000;
        row.IsNew = true;
        if (za)
        {
            row.IsNewShop = row.IsNewGroup = row.IsEquipped = row.IsOwned = true;
            Assert.Equal(0xA000001Fu, row.Flags);
        }
        else Assert.Equal(0xA0000001u, row.Flags);
        Assert.Equal(before, category.Source.Data.ToArray());
        category.Items[1].Value = row.Value;
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        category.Items[1].Value = FashionItem9.None;
        Assert.True(vm.CanSave);
        row.RawValueText = "invalid";
        Assert.True(row.HasErrors);
        Assert.False(vm.CanSave);
        row.RawValueText = "0x12345678";
        Assert.True(vm.CanSave);
        bool closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, category.Source.Data.ToArray());
        vm.SaveCommand.Execute(null);
        using var reloaded = new Fashion9EditorViewModel(save);
        Assert.Equal(row.Value, reloaded.Categories[0].Items[0].Value);
        Assert.Equal(row.Flags, reloaded.Categories[0].Items[0].Flags);
    }
    [Fact]
    public async Task ZaOwnedScopeSkipsEmptyAndHairWhileFiltersMatchEachStatus()
    {
        var save = Load(true); Empty(save);
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        using var vm = new Fashion9EditorViewModel(save, dialogs);
        Assert.Equal(25, vm.Categories.Count);
        foreach (var category in vm.Categories) category.Items[0].Value = 42;
        vm.AllCategories = true;
        await vm.UnlockOwnedCommand.ExecuteAsync(null);
        Assert.Single(dialogs.Confirmations);
        Assert.All(vm.Categories.Where(c => c.SupportsOwned), c => Assert.True(c.Items[0].IsOwned));
        Assert.All(vm.Categories.Where(c => !c.SupportsOwned), c => Assert.False(c.Items[0].IsOwned));
        Assert.All(vm.Categories, c => Assert.False(c.Items[1].IsOwned));
        var row = vm.Categories[0].Items[0];
        row.IsNew = row.IsNewShop = row.IsNewGroup = row.IsEquipped = true;
        for (int filter = 2; filter <= 6; filter++)
        {
            vm.FilterIndex = filter;
            Assert.Contains(row, vm.VisibleItems);
        }
        vm.FilterIndex = 1;
        Assert.All(vm.VisibleItems, r => Assert.True(r.IsEmpty));
        vm.AllCategories = false;
        vm.SelectedCategory = vm.Categories.First(c => !c.SupportsOwned);
        Assert.False(vm.SupportsOwnedAction);
        var hair = vm.SelectedCategory.Items[0];
        hair.Flags = 0xA0000002;
        hair.IsOwned = hair.IsEquipped = hair.IsNewGroup = hair.IsNewShop = true;
        Assert.Equal(0xA0000002u, hair.Flags);
        hair.IsNew = true;
        Assert.Equal(0xA0000003u, hair.Flags);
        vm.ResetCommand.Execute(null);
        Assert.All(vm.Categories.SelectMany(c => c.Items), r => Assert.True(r.IsEmpty));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoOpSavePreservesEveryLoadedCategoryByte(bool za)
    {
        var save = Load(za);
        save.State.Edited = false;
        using var vm = new Fashion9EditorViewModel(save);
        var before = vm.Categories.Select(c => c.Source.Data.ToArray()).ToArray();
        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);
        for (int i = 0; i < before.Length; i++) Assert.Equal(before[i], vm.Categories[i].Source.Data.ToArray());
        Assert.False(save.State.Edited);
    }
    [AvaloniaTheory]
    [InlineData(false, 1.0)]
    [InlineData(true, 1.0)]
    [InlineData(true, 1.5)]
    public void CategoryNavigatorAndStatusControlsFitAndFollowCapabilities(bool za, double scale)
    {
        using var app = new HeadlessAppFixture();
        using var vm = new Fashion9EditorViewModel(Load(za), app.Dialogs);
        app.Window.Width = 1024; app.Window.Height = 720;
        var view = new Fashion9Editor { DataContext = vm };
        app.Window.Content = new LayoutTransformControl { Child = view, LayoutTransform = new ScaleTransform(scale, scale) };
        app.Pump();
        Assert.Equal(za ? 25 : 8, view.GetVisualDescendants().OfType<ListBox>().Single().ItemCount);
        var owned = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.UnlockOwnedCommand));
        var sv = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.UnlockBaseCommand));
        Assert.Equal(za, owned.IsVisible); Assert.Equal(!za, sv.IsVisible);
        var grid = view.FindControl<DataGrid>("ItemGrid")!;
        Assert.True(grid.Bounds.Height > 60);
        vm.Advanced = true; app.Pump();
        Assert.Equal(3, grid.Columns.Count(c => c.IsVisible));
        Assert.True(grid.Bounds.Height > 95); // Room for a header and at least two compact rows.
        Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 50);
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
        {
            var dir = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!; Directory.CreateDirectory(dir);
            Assert.NotNull(app.CaptureFrame(Path.Combine(dir, $"fashion9-{za}-{scale}.png")));
        }
    }
    [AvaloniaFact]
    public void DetailEditorExposesStatusHelpAndKeyboardEditing()
    {
        using var app = new HeadlessAppFixture();
        using var vm = new Fashion9EditorViewModel(Load(true), app.Dialogs);
        app.Window.Width = 900; app.Window.Height = 650;
        var view = new Fashion9Editor { DataContext = vm };
        app.Window.Content = view; app.Pump();
        view.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
        app.Pump();
        var check = view.GetVisualDescendants().OfType<CheckBox>().Single(c =>
            c.IsEffectivelyVisible && Equals(c.Content, LocalizedStrings.Instance["Fashion9Editor_ColumnOwned"]));
        Assert.False(string.IsNullOrWhiteSpace(global::Avalonia.Automation.AutomationProperties.GetHelpText(check)));
        var before = check.IsChecked;
        app.Focus(check); app.PressKey(PhysicalKey.Space);
        Assert.Equal(!before, check.IsChecked);
        Assert.True(vm.CanUndo);
        vm.UndoCommand.Execute(null); app.Pump();
        Assert.Equal(before, vm.SelectedItem!.IsOwned);
        var restored = view.GetVisualDescendants().OfType<CheckBox>().Single(c =>
            c.IsEffectivelyVisible && Equals(c.Content, LocalizedStrings.Instance["Fashion9Editor_ColumnOwned"]));
        Assert.Equal(before, restored.IsChecked);
    }
    [AvaloniaFact]
    public void GermanCategoryLabelsRemainUsableAtScaledViewportWithoutLosingEdits()
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture();
        using var vm = new Fashion9EditorViewModel(Load(true), app.Dialogs);
        var row = vm.Categories[0].Items[0];
        row.IsNew = !row.IsNew;
        var flags = row.Flags;
        try
        {
            app.ViewModel.LanguageService.SetLanguage("de");
            Assert.Equal(flags, row.Flags);
            app.Window.Width = 1024; app.Window.Height = 720;
            var view = new Fashion9Editor { DataContext = vm };
            app.Window.Content = new LayoutTransformControl { Child = view, LayoutTransform = new ScaleTransform(1.5, 1.5) };
            app.Pump();
            var grid = view.FindControl<DataGrid>("ItemGrid")!;
            Assert.True(grid.Bounds.Height > 80);
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.Command, vm.SaveCommand) || ReferenceEquals(b.Command, vm.CancelCommand)))
                Assert.InRange(button.TranslatePoint(new Point(0, button.Bounds.Height), app.Window)!.Value.Y, 1, 720);
            if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
                Assert.NotNull(app.CaptureFrame(Path.Combine(Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!, "fashion9-de-150.png")));
        }
        finally { app.ViewModel.LanguageService.SetLanguage(previous); }
    }

}
