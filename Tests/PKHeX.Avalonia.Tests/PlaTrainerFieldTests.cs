using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class PlaTrainerFieldTests
{
    internal static SAV8LA LoadSave() => Assert.IsType<SAV8LA>(Fixtures.SaveFileFixture.LoadSave(
        Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8a_legendsarceus.main")));
    [Fact]
    public void ValidSatchelFortyAndEveryUnchangedBlockSurviveNoOpAndReset()
    {
        var save = LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => (b.Type, Data: b.Data.ToArray()));
        using var vm = new Misc8aEditorViewModel(save);
        Assert.Equal(40u, vm.SatchelUpgrades); Assert.True(vm.SatchelMaximum >= vm.SatchelUpgrades);
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        vm.SatchelUpgrades = 41; vm.X += 1; vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) { Assert.Equal(before[b.Key].Type, b.Type); Assert.Equal(before[b.Key].Data, b.Data.ToArray()); }
    }
    [Fact]
    public void ProgressionMapAndHumanDatesRoundtripOnlyAfterApply()
    {
        var save = LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new Misc8aEditorViewModel(save);
        vm.TrainerName = "Tester"; vm.MeritCurrent = 123; vm.MeritEarned = 456; vm.Rank = 10; vm.SatchelUpgrades = 40;
        vm.MapName = "ha_area00_s02"; vm.X = 10; vm.Y = -20; vm.Z = 30; vm.Rotation = 135;
        vm.StartedDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero); vm.StartedTime = new TimeSpan(5, 22, 0); vm.StartedSeconds = 22;
        vm.LastSavedDate = vm.StartedDate; vm.LastSavedTime = new TimeSpan(6, 2, 0);
        Assert.True(vm.CanSave); foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        var reopened = Assert.IsType<SAV8LA>(SaveUtil.GetSaveFile(save.Write())); using var check = new Misc8aEditorViewModel(reopened);
        Assert.Equal("Tester", check.TrainerName); Assert.Equal(123u, check.MeritCurrent); Assert.Equal(456u, check.MeritEarned);
        Assert.Equal(10u, check.Rank); Assert.Equal(40u, check.SatchelUpgrades); Assert.Equal("ha_area00_s02", check.MapName);
        Assert.Equal(-20, check.Y); Assert.Equal(135, check.Rotation, 4); Assert.Equal(22, check.StartedSeconds); Assert.Equal(vm.LastSavedTime, check.LastSavedTime);
    }
    [Fact]
    public void ConflictsAndInvalidMapRejectTheEntireTransactionAndCloseDiscards()
    {
        var save = LoadSave(); using var vm = new Misc8aEditorViewModel(save);
        vm.MeritEarned++; vm.X++; save.Blocks.SetBlockValue(SaveBlockAccessor8LA.KMeritEarnedTotal, vm.MeritEarned + 1);
        var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        vm.SaveCommand.Execute(null); Assert.True(vm.HasError);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.ResetCommand.Execute(null); vm.MapName = "invalid\0map"; vm.MeritCurrent++; Assert.False(vm.CanSave);
        vm.SaveCommand.Execute(null); foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.ResetCommand.Execute(null); vm.MeritEarned++; vm.Dispose(); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
}
