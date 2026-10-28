using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Gen7TrainerFieldTests
{
    [Theory] [InlineData(GameVersion.SN)] [InlineData(GameVersion.US)]
    public void TrainerRegionsCoordinatesAppearanceAndDatesRoundtripWithStaging(GameVersion version)
    {
        var file = version == GameVersion.SN ? "gen7_sun.main" : "gen7_ultrasun.main";
        var save = Assert.IsAssignableFrom<SAV7>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, file)));
        var before = save.Data.ToArray();
        using var vm = new Misc7EditorViewModel(save);
        vm.TrainerName = "Tester"; vm.Country = 49; vm.SubRegion = 2; vm.ConsoleRegion = 2; vm.BattlePoints = 33;
        vm.FestivalCoins = 100; vm.FestivalName = "Test Plaza"; vm.AlolaOffset = 43200;
        vm.MapId = 123; vm.X = 1.5; vm.Y = -2.5; vm.Z = 3; vm.Rotation = 90;
        vm.SkinColor = 3; vm.DaysFromRefresh = 45; vm.BattleStyle = 2; vm.MegaUnlocked = true; vm.ZMoveUnlocked = true;
        vm.StartedDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero); vm.StartedTime = new TimeSpan(12, 13, 0); vm.StartedSecond = 14;
        vm.FameDate = vm.StartedDate; vm.FameTime = new TimeSpan(14, 15, 0); vm.FameSecond = 16;
        vm.LastSavedDate = vm.StartedDate; vm.LastSavedTime = new TimeSpan(18, 19, 0);
        Assert.True(vm.CanSave); Assert.Equal(before, save.Data.ToArray()); vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        Assert.Equal("Tester", save.OT); Assert.Equal(49, save.Country); Assert.Equal(2, save.Region); Assert.Equal(2, save.ConsoleRegion);
        Assert.Equal(33u, save.Misc.BP); Assert.Equal(100, save.Festa.FestaCoins); Assert.Equal("Test Plaza", save.Festa.FestivalPlazaName);
        Assert.Equal(43200ul, save.GameTime.AlolaTime); Assert.Equal(123, save.Situation.M);
        Assert.Equal(90f, save.Situation.X); Assert.Equal(-150f, save.Situation.Y); Assert.Equal(180f, save.Situation.Z);
        Assert.Equal(save.Situation.X, save.Overworld.X); Assert.Equal(save.Situation.RZ, save.Overworld.RZ);
        Assert.Equal(3, save.MyStatus.DressUpSkinColor); Assert.Equal(45, save.Misc.DaysFromRefreshed); Assert.Equal(2, save.MyStatus.BallThrowType);
        Assert.True(save.MyStatus.MegaUnlocked); Assert.True(save.MyStatus.ZMoveUnlocked);
        var expected = new DateTime(2026, 10, 3, 12, 13, 14);
        Assert.Equal((uint)(expected - new DateTime(2000, 1, 1)).TotalSeconds, save.SecondsToStart);
        var after = save.Data.ToArray(); vm.SaveCommand.Execute(null); Assert.Equal(after, save.Data.ToArray());
        var reopened = Assert.IsAssignableFrom<SAV7>(SaveUtil.GetSaveFile(save.Write()));
        using var check = new Misc7EditorViewModel(reopened); Assert.Equal(1.5, check.X); Assert.Equal(90, check.Rotation, 4);
        Assert.Equal(14, check.StartedSecond); Assert.Equal(16, check.FameSecond);
    }

    [Fact]
    public void NaNCoordinatesAndUnsetDatesSurviveNoOpWhileInvalidNewValuesBlockApply()
    {
        var save = new SAV7SM(); save.Situation.X = BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234));
        var before = save.Data.ToArray(); using var vm = new Misc7EditorViewModel(save);
        Assert.Null(vm.StartedDate); Assert.True(vm.CanSave); vm.SaveCommand.Execute(null); Assert.Equal(before, save.Data.ToArray());
        vm.StartedDate = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); vm.StartedTime = TimeSpan.Zero;
        Assert.False(vm.CanSave); vm.SaveCommand.Execute(null); Assert.True(vm.HasError); Assert.Equal(before, save.Data.ToArray());
    }
}
