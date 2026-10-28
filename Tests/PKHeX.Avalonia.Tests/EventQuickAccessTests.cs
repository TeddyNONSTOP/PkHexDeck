using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class EventQuickAccessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialFlagAndGridShareStagingUntilApply(bool ultra)
    {
        SAV7 save = ultra ? new SAV7USUM() : new SAV7SM();
        save.EventWork.SetEventFlag(0, true);
        save.State.Edited = false;
        var before = save.Data.ToArray();
        var vm = new EventFlagsEditorViewModel(save);
        Assert.True(vm.SelectedFlagValue);
        vm.SelectedFlagValue = false;
        Assert.False(vm.Flags[0].IsSet);
        Assert.True(save.EventWork.GetEventFlag(0));
        Assert.Equal(before, save.Data.ToArray());
        vm.ResetCommand.Execute(null);
        Assert.True(vm.SelectedFlagValue);
        Assert.True(vm.Flags[0].IsSet);
        vm.Flags[0].IsSet = false;
        Assert.False(vm.SelectedFlagValue);
        vm.SaveCommand.Execute(null);
        Assert.False(save.EventWork.GetEventFlag(0));
        Assert.True(save.State.Edited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastIndexIsValidAndInvalidIndicesAreClamped(bool ultra)
    {
        SAV7 save = ultra ? new SAV7USUM() : new SAV7SM();
        var vm = new EventFlagsEditorViewModel(save);
        Assert.Equal(vm.FlagCount - 1, vm.MaxFlagIndex);
        vm.SelectedFlagIndex = vm.FlagCount;
        Assert.Equal(vm.MaxFlagIndex, vm.SelectedFlagIndex);
        vm.SelectedFlagValue = true;
        Assert.True(vm.Flags[^1].IsSet);
        Assert.False(save.EventWork.GetEventFlag(vm.MaxFlagIndex));
        vm.SelectedFlagIndex = -1;
        Assert.Equal(0, vm.SelectedFlagIndex);
        vm.SaveCommand.Execute(null);
        Assert.True(save.EventWork.GetEventFlag(vm.MaxFlagIndex));
    }

    [Fact]
    public void SelectingOtherRowDoesNotOverwriteEitherStagedValue()
    {
        var save = new SAV7SM();
        var vm = new EventFlagsEditorViewModel(save);
        vm.SelectedFlagValue = true;
        vm.SelectedFlagIndex = 1;
        Assert.False(vm.SelectedFlagValue);
        Assert.True(vm.Flags[0].IsSet);
        vm.SelectedFlagValue = true;
        vm.SelectedFlagIndex = 0;
        Assert.True(vm.SelectedFlagValue);
        Assert.True(vm.Flags[1].IsSet);
        vm.ResetCommand.Execute(null);
        Assert.False(vm.SelectedFlagValue);
        Assert.False(vm.Flags[1].IsSet);
    }
}
