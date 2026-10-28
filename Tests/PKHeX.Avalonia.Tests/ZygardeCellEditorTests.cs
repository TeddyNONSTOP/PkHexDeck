using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using Xunit;

namespace PKHeX.Avalonia.Tests;

public sealed class ZygardeCellEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditsAndBulkActions_AreStagedAndDiscarded(bool ultra)
    {
        SAV7 save = ultra ? new SAV7USUM() : new SAV7SM();
        save.EventWork.ZygardeCellTotal = 3;
        save.EventWork.ZygardeCellCount = 5;
        var before = save.Data.ToArray();
        var vm = new ZygardeCellEditorViewModel(save);
        vm.Cells[0].State = 2;
        Assert.Equal(6, vm.CellsCollected);
        Assert.Equal(ultra ? 3 : 4, vm.CellsTotal);
        vm.CollectAllCommand.Execute(null);
        Assert.Equal(before, save.Data);
        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data);
        Assert.Equal(5, vm.CellsCollected);
        Assert.Equal(3, vm.CellsTotal);
        vm.CellsTotal = 8;
        vm.CellsCollected = 9;
        vm.Cells[0].State = 2;
        vm.SaveCommand.Execute(null);
        var reopened = new ZygardeCellEditorViewModel(save);
        Assert.Equal(10, reopened.CellsCollected);
        Assert.Equal(ultra ? 8 : 9, reopened.CellsTotal);
        Assert.Equal(2, reopened.Cells[0].State);
        vm.CellsCollected = -1;
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.True(vm.HasCounterWarning);
    }

    [Fact]
    public void SunMoon_UsesZygardeCellSemantics()
    {
        var sav = new SAV7SM();
        var vm = new ZygardeCellEditorViewModel(sav);

        Assert.True(vm.IsSupported);
        Assert.True(vm.IsZygardeCell);
        Assert.False(vm.IsTotemSticker);
        Assert.Equal(95, vm.Cells.Count);

        vm.CollectAllCommand.Execute(null);

        Assert.Equal(95, vm.CellsCollected);
        Assert.Equal(95, vm.CellsTotal);
        Assert.All(vm.Cells, cell => Assert.Equal(2, cell.State));
        vm.SaveCommand.Execute(null);
        Assert.True(sav.State.Edited);
    }

    [Fact]
    public void SunMoon_RowEdits_RecalculateTotalsAndCollectedCount()
    {
        var sav = new SAV7SM();
        var vm = new ZygardeCellEditorViewModel(sav);

        vm.Cells[0].State = 1;
        Assert.Equal(0, vm.CellsTotal);
        Assert.Equal(0, vm.CellsCollected);

        vm.Cells[0].State = 2;
        Assert.Equal(1, vm.CellsTotal);
        Assert.Equal(1, vm.CellsCollected);

        vm.Cells[0].State = 0;
        Assert.Equal(0, vm.CellsTotal);
        Assert.Equal(0, vm.CellsCollected);
    }

    [Fact]
    public void UltraSunMoon_UsesTotemStickerSemantics()
    {
        var sav = new SAV7USUM();
        var vm = new ZygardeCellEditorViewModel(sav);

        Assert.True(vm.IsSupported);
        Assert.True(vm.IsTotemSticker);
        Assert.False(vm.IsZygardeCell);
        Assert.Equal(100, vm.Cells.Count);

        vm.CollectAllCommand.Execute(null);

        Assert.Equal(100, vm.CellsCollected);
        Assert.Equal(0, vm.CellsTotal);
        Assert.All(vm.Cells, cell => Assert.Equal(2, cell.State));
        Assert.Equal(0, sav.GetRecord(72));
        vm.SaveCommand.Execute(null);
        Assert.Equal(100, sav.GetRecord(72));
        vm.SaveCommand.Execute(null);
        Assert.True(sav.State.Edited);
    }

    [Fact]
    public void UltraSunMoon_RowEdits_RecalculateCollectedCountWithoutZygardeTotal()
    {
        var sav = new SAV7USUM();
        var vm = new ZygardeCellEditorViewModel(sav);

        vm.Cells[0].State = 2;

        Assert.Equal(1, vm.CellsCollected);
        Assert.Equal(0, vm.CellsTotal);
        Assert.Equal(0, sav.GetRecord(72));
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, sav.GetRecord(72));
    }

    [Fact]
    public void UltraSunMoon_CounterWarningUsesStickerSemanticsInBothDirections()
    {
        var vm = new ZygardeCellEditorViewModel(new SAV7USUM());

        Assert.False(vm.HasCounterWarning);
        vm.Cells[0].State = 2;
        Assert.Equal(1, vm.CellsCollected);
        Assert.False(vm.HasCounterWarning);

        vm.CellsCollected = 0;
        Assert.True(vm.HasCounterWarning);
        Assert.False(vm.ShowCellCounterWarning);
        Assert.True(vm.ShowTotemStickerCounterWarning);

        vm.CellsCollected = 2;
        Assert.True(vm.HasCounterWarning);
        vm.CellsCollected = 1;
        Assert.False(vm.HasCounterWarning);
    }

    [Fact]
    public void SunMoon_CounterWarningAllowsCellsConsumedByAssembly()
    {
        var vm = new ZygardeCellEditorViewModel(new SAV7SM());
        vm.Cells[0].State = 2;

        Assert.False(vm.HasCounterWarning);
        vm.CellsCollected = 2; // A collected cell may already have been spent on Zygarde assembly.
        Assert.False(vm.HasCounterWarning);
        vm.CellsCollected = 0;
        Assert.True(vm.HasCounterWarning);
        Assert.True(vm.ShowCellCounterWarning);
        Assert.False(vm.ShowTotemStickerCounterWarning);
    }
}
