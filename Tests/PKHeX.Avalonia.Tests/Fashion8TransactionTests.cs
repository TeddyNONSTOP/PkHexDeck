using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Fashion8TransactionTests
{
    private static SAV8SWSH Load(byte gender = 0, GameVersion version = GameVersion.SW)
    {
        // Core's allocated in-memory fixture supports direct fashion-block testing.
        // The older committed Sword corpus file contains unparseable None-type blocks.
        var save = new SAV8SWSH();
        var owned = save.Fashion.GetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR);
        owned[22] = true;
        save.Fashion.SetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR, owned);
        save.MyStatus.GenderAppearance = gender;
        save.Version = version;
        return save;
    }
    [Theory]
    [InlineData(0, GameVersion.SW)]
    [InlineData(1, GameVersion.SW)]
    [InlineData(0, GameVersion.SH)]
    [InlineData(1, GameVersion.SH)]
    public async Task AllBulkActionsMatchCoreAndCanBeCancelledOrUndone(byte gender, GameVersion version)
    {
        foreach (var action in new[] { "all", "legal", "reset", "clear" })
        {
            var save = Load(gender, version);
            var before = save.Fashion.Data.ToArray();
            var expected = (SAV8SWSH)save.Clone();
            switch (action)
            {
                case "all": expected.Fashion.UnlockAll(); break;
                case "legal": expected.Fashion.UnlockAllLegal(); break;
                case "reset": expected.Fashion.Clear(); expected.Fashion.Reset(); break;
                case "clear": expected.Fashion.Clear(); break;
            }
            var dialogs = new RecordingDialogService { ConfirmResult = true };
            var undo = new UndoRedoService();
            undo.Initialize(save);
            using var vm = new FashionEditorViewModel(save, dialogs, undo);
            switch (action)
            {
                case "all": await vm.UnlockAllCommand.ExecuteAsync(null); break;
                case "legal": await vm.UnlockAllLegalCommand.ExecuteAsync(null); break;
                case "reset": await vm.ResetCommand.ExecuteAsync(null); break;
                case "clear": await vm.ClearCommand.ExecuteAsync(null); break;
            }
            Assert.Single(dialogs.Confirmations);
            Assert.Equal(before, save.Fashion.Data.ToArray());
            Assert.True(vm.CanUndoPreview);
            vm.UndoPreviewCommand.Execute(null);
            Assert.False(vm.CanUndoPreview);
            // Repeat then commit the reviewed snapshot into application history.
            switch (action)
            {
                case "all": await vm.UnlockAllCommand.ExecuteAsync(null); break;
                case "legal": await vm.UnlockAllLegalCommand.ExecuteAsync(null); break;
                case "reset": await vm.ResetCommand.ExecuteAsync(null); break;
                case "clear": await vm.ClearCommand.ExecuteAsync(null); break;
            }
            vm.SaveCommand.Execute(null);
            Assert.Equal(expected.Fashion.Data.ToArray(), save.Fashion.Data.ToArray());
            if (before.AsSpan().SequenceEqual(expected.Fashion.Data)) continue;
            Assert.True(undo.CanUndo);
            undo.Undo();
            Assert.Equal(before, save.Fashion.Data.ToArray());
            undo.Redo();
            Assert.Equal(expected.Fashion.Data.ToArray(), save.Fashion.Data.ToArray());
        }
    }
    [Fact]
    public async Task RejectedConfirmationAndWindowCloseNeverMutateSource()
    {
        var save = Load();
        var before = save.Fashion.Data.ToArray();
        var dialogs = new RecordingDialogService();
        using var vm = new FashionEditorViewModel(save, dialogs);
        await vm.ClearCommand.ExecuteAsync(null);
        Assert.False(vm.CanUndoPreview);
        dialogs.ConfirmResult = true;
        await vm.ClearCommand.ExecuteAsync(null);
        bool closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, save.Fashion.Data.ToArray());
        vm.RefreshCommand.Execute(null);
        Assert.False(vm.CanUndoPreview);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Fashion.Data.ToArray());
    }
    [Fact]
    public void UnknownFlagsRoundTripAndAvailabilityFiltersUseCoreMasks()
    {
        var save = Load();
        var owned = save.Fashion.GetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR);
        owned[1000] = true;
        save.Fashion.SetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR, owned);
        var before = save.Fashion.Data.ToArray();
        using var vm = new FashionEditorViewModel(save, new RecordingDialogService());
        var row = vm.SelectedRegion!.Items[1000];
        Assert.False(row.IsKnown);
        Assert.False(row.IsLegal);
        Assert.True(row.IsOwned);
        row.IsNew = !row.IsNew;
        Assert.Equal(before, save.Fashion.Data.ToArray());
        vm.SaveCommand.Execute(null);
        Assert.True(save.Fashion.GetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR)[1000]);
        Assert.Equal(row.IsNew, save.Fashion.GetArrayNewFlag(FashionUnlock8.REGION_EYEWEAR)[1000]);
        vm.FilterIndex = 5;
        Assert.Contains(row, vm.VisibleItems);
        vm.SearchText = "1000";
        Assert.Single(vm.VisibleItems);
        vm.SearchText = string.Empty;
        vm.FilterIndex = 3;
        Assert.All(vm.VisibleItems, item => Assert.True(item.IsLegal));
        var legal = (SAV8SWSH)save.Clone();
        legal.Fashion.Clear(); legal.Fashion.UnlockAllLegal();
        var mask = legal.Fashion.GetArrayOwnedFlag(FashionUnlock8.REGION_EYEWEAR);
        Assert.Equal(mask.Count(b => b), vm.VisibleItems.Count);
    }
    [Fact]
    public void SaveBlockHistoryInterleavesWithSlotsAndRejectsAnotherSave()
    {
        var save = Load();
        var undo = new UndoRedoService();
        undo.Initialize(save);
        var slot = new SlotInfoBox(0, 0, save);
        var pokemonBefore = slot.Read(save).Species;
        var replacement = save.BlankPKM;
        replacement.Species = 25;
        undo.AddChange(slot);
        slot.WriteTo(save, replacement, EntityImportSettings.None);
        var before = save.Fashion.Data.ToArray();
        var after = before.ToArray();
        after[800] ^= 1;
        Assert.True(undo.ApplyBlockChange(save, () => save.Fashion.Data.ToArray(), data => data.CopyTo(save.Fashion.Data), after));
        undo.Undo();
        Assert.Equal(before, save.Fashion.Data.ToArray());
        Assert.Equal(25, slot.Read(save).Species);
        undo.Undo();
        Assert.Equal(pokemonBefore, slot.Read(save).Species);
        undo.Redo();
        Assert.Equal(25, slot.Read(save).Species);
        undo.Redo();
        Assert.Equal(after, save.Fashion.Data.ToArray());
        Assert.Throws<InvalidOperationException>(() => undo.ApplyBlockChange(Load(), () => before, _ => { }, after));
    }
    [Fact]
    public void FailedBlockWriteRollsBackWithoutCreatingHistory()
    {
        var save = Load();
        var undo = new UndoRedoService();
        undo.Initialize(save);
        var bytes = new byte[] { 1, 2 };
        int calls = 0;
        Assert.Throws<IOException>(() => undo.ApplyBlockChange(save, () => bytes, data =>
        {
            bytes = data;
            if (++calls == 1) throw new IOException();
        }, new byte[] { 3, 4 }));
        Assert.Equal(new byte[] { 1, 2 }, bytes);
        Assert.False(undo.CanUndo);
        Assert.Equal(0, undo.ChangeCount);
    }
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ReviewGridAndCommitActionsFitAndKeyboardToggleWorks(double scale)
    {
        using var app = new HeadlessAppFixture();
        using var vm = new FashionEditorViewModel(Load(), app.Dialogs);
        app.Window.Width = 1024;
        app.Window.Height = 720;
        var view = new FashionEditorView { DataContext = vm };
        app.Window.Content = new LayoutTransformControl { Child = view, LayoutTransform = new ScaleTransform(scale, scale) };
        app.Pump();
        var grid = view.FindControl<DataGrid>("FashionGrid")!;
        Assert.True(grid.Bounds.Height > 80);
        Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 40);
        var checkbox = grid.GetVisualDescendants().OfType<CheckBox>().First();
        Assert.NotEmpty(AutomationProperties.GetName(checkbox)!);
        var before = checkbox.IsChecked;
        app.Focus(checkbox);
        app.PressKey(PhysicalKey.Space);
        Assert.Equal(!before, checkbox.IsChecked);
        foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.Command, vm.SaveCommand) || ReferenceEquals(b.Command, vm.CancelCommand)))
            Assert.InRange(button.TranslatePoint(new Point(0, button.Bounds.Height), app.Window)!.Value.Y, 1, 720);
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
        {
            var dir = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!;
            Directory.CreateDirectory(dir);
            Assert.NotNull(app.CaptureFrame(Path.Combine(dir, $"fashion8-{scale}.png")));
        }
    }
    [Fact]
    public void FailedBlockUndoRetainsHistoryAndRestoresCurrentBytes()
    {
        var save = Load();
        var undo = new UndoRedoService(); undo.Initialize(save);
        var bytes = new byte[] { 1, 2 };
        bool fail = false;
        undo.ApplyBlockChange(save, () => bytes, data =>
        {
            bytes = data;
            if (fail) { fail = false; throw new IOException(); }
        }, new byte[] { 3, 4 });
        fail = true;
        Assert.Throws<IOException>(() => undo.Undo());
        Assert.Equal(new byte[] { 3, 4 }, bytes);
        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
        undo.Undo();
        Assert.Equal(new byte[] { 1, 2 }, bytes);
        fail = true;
        Assert.Throws<IOException>(() => undo.Redo());
        Assert.Equal(new byte[] { 1, 2 }, bytes);
        Assert.True(undo.CanRedo);
        undo.Redo();
        Assert.Equal(new byte[] { 3, 4 }, bytes);
    }

}
