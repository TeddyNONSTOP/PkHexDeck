using System.Buffers.Binary;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Gen7MiscTransactionTests
{
    [Theory] [InlineData(GameVersion.SN)] [InlineData(GameVersion.US)]
    public void FinderAndFlagsStayStagedAndResetOrCloseDiscard(GameVersion version)
    {
        var save = Assert.IsAssignableFrom<SAV7>(BlankSaveFile.Get(version)); var before = save.Data.ToArray();
        using var vm = new Misc7EditorViewModel(save); vm.SnapCount = 12; vm.Stamps[0].IsObtained = true;
        Assert.Equal(before, save.Data.ToArray()); vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray()); vm.SnapCount = 33; vm.Dispose(); vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());
    }

    [Theory] [InlineData(GameVersion.SN)] [InlineData(GameVersion.US)]
    public void NoOpPreservesUnknownCameraAndNoncanonicalBooleanStorage(GameVersion version)
    {
        var save = Assert.IsAssignableFrom<SAV7>(BlankSaveFile.Get(version));
        save.PokeFinder.CameraVersion = 65000; BinaryPrimitives.WriteUInt16LittleEndian(save.PokeFinder.Data[2..], 2);
        save.BattleTree.SetTreeStreak(65535, 0, false, false); var before = save.Data.ToArray(); save.State.Edited = false;
        using var vm = new Misc7EditorViewModel(save); Assert.Contains(vm.CameraVersions, item => item.Value == 65000);
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError); Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
    }

    [Fact]
    public void CommitPreservesOtherLiveBlocksAndRejectsAChangedFinderBlock()
    {
        var save = new SAV7USUM(); using var vm = new Misc7EditorViewModel(save);
        vm.SnapCount = 12; save.Money = 123; vm.SaveCommand.Execute(null);
        Assert.False(vm.HasError); Assert.Equal(12u, save.PokeFinder.SnapCount); Assert.Equal(123u, save.Money);
        vm.SnapCount = 21; vm.SuperSingleUnlocked = true; save.PokeFinder.SnapCount = 99; var before = save.Data.ToArray();
        vm.SaveCommand.Execute(null); Assert.True(vm.HasError); Assert.Equal(before, save.Data.ToArray());
    }
}
