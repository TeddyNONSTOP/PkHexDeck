using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class BdspTrainerFoundationTests
{
    [Fact]
    public void InvalidDateNaNGeometryAndRivalTrashSurviveNoOpWhileInvalidEditsRejectAllWrites()
    {
        var save = LoadSave(); save.System.TicksStart = -1; save.System.TicksLatest = 0;
        save.MyStatus.Height = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234));
        save.RivalNameTrash[^1] = 0x7f;
        var before = save.Data.ToArray(); using var vm = new BdspTrainerEditorViewModel(save);
        Assert.Null(vm.StartedDate); Assert.Null(vm.LastSavedDate); Assert.True(float.IsNaN(vm.Y));
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error); Assert.Equal(before, save.Data.ToArray());
        vm.YText = "invalid"; vm.RivalName = "Blocked"; vm.SaveCommand.Execute(null); Assert.NotEmpty(vm.Error); Assert.Equal(before, save.Data.ToArray());
        vm.ResetCommand.Execute(null); Assert.True(vm.CanSave);
    }
    [Fact]
    public void ConcurrentHighByteCoordinateChangeRejectsTheWholeTransaction()
    {
        var save = LoadSave(); using var vm = new BdspTrainerEditorViewModel(save);
        vm.X = save.MyStatus.X + 1; vm.RivalName = "Pending"; save.MyStatus.X += 256;
        var live = save.Data.ToArray(); vm.SaveCommand.Execute(null); Assert.NotEmpty(vm.Error); Assert.Equal(live, save.Data.ToArray());
    }
    [Fact]
    public void DeliberateVersionChangeRoundtripsWithoutTouchingUneditedIdentity()
    {
        var save = LoadSave(); var nameTrash = save.MyStatus.OriginalTrainerTrash.ToArray();
        using var vm = new BdspTrainerEditorViewModel(save); vm.Version = GameVersion.SP; vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        var reopened = Assert.IsType<SAV8BS>(SaveUtil.GetSaveFile(save.Write())); Assert.Equal(GameVersion.SP, reopened.Version);
        Assert.Equal(nameTrash, reopened.MyStatus.OriginalTrainerTrash.ToArray());
    }
    [Fact]
    public void DatesPreserveSubSecondFiletimeTicksAndRecordsRoundtrip()
    {
        var save = LoadSave(); save.System.TicksStart += 12345; save.System.TicksLatest += 23456;
        long startFraction = save.System.TicksStart % TimeSpan.TicksPerSecond, lastFraction = save.System.TicksLatest % TimeSpan.TicksPerSecond;
        var before = save.Data.ToArray(); using var vm = new BdspTrainerEditorViewModel(save);
        Assert.Equal(20211111, vm.TrainerRecords.Single(record => record.Id == 0).Value);
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error); Assert.Equal(before, save.Data.ToArray());
        vm.StartedSeconds = (vm.StartedSeconds + 1) % 60; vm.LastSavedSeconds = (vm.LastSavedSeconds + 1) % 60;
        vm.Bp = 123; vm.TrainerRecords.Single(record => record.Id == 0).Value = 20260101;
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        Assert.Equal(startFraction, save.System.TicksStart % TimeSpan.TicksPerSecond); Assert.Equal(lastFraction, save.System.TicksLatest % TimeSpan.TicksPerSecond);
        var reopened = Assert.IsType<SAV8BS>(SaveUtil.GetSaveFile(save.Write())); Assert.Equal(123u, reopened.BattleTower.BP); Assert.Equal(20260101, reopened.GetRecord(0));
    }
    private static SAV8BS LoadSave() => Assert.IsType<SAV8BS>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8b_brilliantdiamond.bin")));
    [Fact]
    public void NativeFixtureIdentityBadgesAndMapValuesLoadAndNoOpPreservesBytes()
    {
        var save = LoadSave(); var before = save.Data.ToArray(); using var vm = new BdspTrainerEditorViewModel(save);
        Assert.Equal("RoC", vm.TrainerName); Assert.Equal("Kenny", vm.RivalName); Assert.Equal(794936u, vm.DisplayTid); Assert.Equal(4145u, vm.DisplaySid);
        Assert.Equal((short)350, vm.ZoneId); Assert.Equal(6, vm.X); Assert.Equal(0, vm.Y); Assert.Equal(6, vm.Height);
        Assert.All(vm.Badges, badge => Assert.True(badge.Value));
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error); Assert.Equal(before, save.Data.ToArray());
    }
    [Fact]
    public void ExpandedDuplicateIdentityUpdatesOnlyMatchingTrainerRecords()
    {
        var save = LoadSave(); Assert.True(save.HasFirstSaveFileExpansion);
        var own = save.RecordAdd.GetRecord(0); own.OT = save.OT; own.ID32 = save.ID32;
        var other = save.RecordAdd.GetRecord(1); other.OT = "Other"; other.ID32 = 123;
        using var vm = new BdspTrainerEditorViewModel(save); vm.TrainerName = "Tester"; vm.DisplayTid = 123456; vm.DisplaySid = 100;
        vm.RivalName = "Rival"; vm.X = -10; vm.Badges[0].Value = false;
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        Assert.Equal("Tester", own.OT); Assert.Equal(save.ID32, own.ID32); Assert.Equal("Other", other.OT); Assert.Equal(123u, other.ID32);
        var reopened = Assert.IsType<SAV8BS>(SaveUtil.GetSaveFile(save.Write())); Assert.Equal("Rival", reopened.RivalName); Assert.Equal(-10, reopened.MyStatus.X);
        Assert.False(reopened.FlagWork.GetSystemFlag(124));
    }
    [Fact]
    public void ResetAndCloseDiscardDraftsAndConcurrentIdentityRejectsAllWrites()
    {
        var save = LoadSave(); var before = save.Data.ToArray(); using var vm = new BdspTrainerEditorViewModel(save);
        vm.RivalName = "Discard"; vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null); Assert.Equal(before, save.Data.ToArray());
        vm.TrainerName = "Pending"; vm.X = 999; save.OT = "Live"; var live = save.Data.ToArray();
        vm.SaveCommand.Execute(null); Assert.NotEmpty(vm.Error); Assert.Equal(live, save.Data.ToArray());
        vm.Dispose(); vm.SaveCommand.Execute(null); Assert.Equal(live, save.Data.ToArray());
    }
}
