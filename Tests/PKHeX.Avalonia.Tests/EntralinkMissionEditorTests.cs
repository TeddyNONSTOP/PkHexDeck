using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class EntralinkMissionEditorTests
{
    private static SAV5B2W2 LoadWhite2()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../savefiles/gen5_white2.sav"));
        return Assert.IsType<SAV5B2W2>(FileUtil.GetSupportedFile(path));
    }

    [Fact]
    public void MissionEditsStageUntilSaveAndRoundTripThroughTheSave()
    {
        var save = LoadWhite2();
        var before = save.Data.ToArray();
        var editedBefore = save.State.Edited;
        var vm = new EntralinkEditorViewModel(save);
        Assert.Equal(FestaBlock5.MaxMissionIndex + 1, vm.Missions.Count);

        var mission = vm.Missions[2];
        Assert.False(mission.IsUnlocked);
        Assert.Contains(vm.Missions, row => row.IsUnlocked);
        mission.BestTotal = 321;
        mission.BestScore = 654;
        mission.Level = 3;
        mission.IsNew = true;
        vm.FestaMostParticipants = 24;
        vm.SelectedMission = mission;
        vm.UnlockSelectedMissionCommand.Execute(null);
        Assert.True(mission.IsUnlocked);
        Assert.Equal(before, save.Data.ToArray());
        Assert.Equal(editedBefore, save.State.Edited);
        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());

        vm = new EntralinkEditorViewModel(save);
        mission = vm.Missions[2];
        mission.BestTotal = 321;
        mission.BestScore = 654;
        mission.Level = 3;
        mission.IsNew = true;
        vm.FestaMostParticipants = 24;
        vm.SelectedMission = mission;
        vm.UnlockSelectedMissionCommand.Execute(null);
        var unrelatedFlag = !save.EventWork.GetEventFlag(0);
        save.EventWork.SetEventFlag(0, unrelatedFlag);
        save.EventWork.SetWork(0, 12345);
        vm.SaveCommand.Execute(null);

        var reloaded = new SAV5B2W2(save.Write().ToArray());
        var record = reloaded.Festa.GetMissionRecord(2);
        Assert.Equal(321, record.Total);
        Assert.Equal(654, record.Score);
        Assert.Equal(3, record.Level);
        Assert.True(record.IsNew);
        Assert.Equal(24, reloaded.Festa.Participants);
        Assert.True(reloaded.Festa.IsFunfestMissionUnlocked(2));
        Assert.Equal(unrelatedFlag, reloaded.EventWork.GetEventFlag(0));
        Assert.Equal(12345, reloaded.EventWork.GetWork(0));
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void BulkUnlockIncludesLastMissionAndCanBeCancelled()
    {
        var save = LoadWhite2();
        var before = save.Data.ToArray();
        var vm = new EntralinkEditorViewModel(save);
        vm.UnlockAllMissionsCommand.Execute(null);
        Assert.All(vm.Missions.Skip(1), mission => Assert.True(mission.IsUnlocked));
        Assert.Equal(before, save.Data.ToArray());
        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());

        vm = new EntralinkEditorViewModel(save);
        vm.UnlockAllMissionsCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        var reloaded = new SAV5B2W2(save.Write().ToArray());
        Assert.True(reloaded.Festa.IsFunfestMissionUnlocked(FestaBlock5.MaxMissionIndex));
    }

    [Fact]
    public void BlackWhiteSaveHasNoFunfestMissionList()
    {
        var vm = new EntralinkEditorViewModel(new SAV5BW());
        Assert.False(vm.IsB2W2);
        Assert.Empty(vm.Missions);
    }

    [Fact]
    public void OpeningAndSavingWithoutChangesPreservesEncryptedForestAndEditedFlag()
    {
        var save = LoadWhite2();
        var before = save.Data.ToArray();
        var editedBefore = save.State.Edited;
        var vm = new EntralinkEditorViewModel(save);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());
        Assert.Equal(editedBefore, save.State.Edited);
    }

    [Fact]
    public void ExistingGeneralFieldsAlsoUseSaveAndCancel()
    {
        var save = LoadWhite2();
        var before = save.Data.ToArray();
        var targetLevel = save.Entralink.WhiteForestLevel + 1;
        var vm = new EntralinkEditorViewModel(save);
        vm.WhiteLevel = targetLevel;
        vm.FestaHosted = 42;
        Assert.Equal(before, save.Data.ToArray());
        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());

        vm = new EntralinkEditorViewModel(save);
        vm.WhiteLevel = targetLevel;
        vm.FestaHosted = 42;
        vm.SaveCommand.Execute(null);
        var reloaded = new SAV5B2W2(save.Write().ToArray());
        Assert.Equal(targetLevel, reloaded.Entralink.WhiteForestLevel);
        Assert.Equal((ushort)42, reloaded.Festa.Hosted);
    }

    [AvaloniaFact]
    public void MissionMasterDetailIsReachableAtCompactDialogSize()
    {
        var view = new EntralinkEditor
        {
            DataContext = new EntralinkEditorViewModel(new SAV5B2W2()),
            Width = 640,
            Height = 550,
        };
        var window = new Window { Content = view, Width = 650, Height = 560 };
        try
        {
            window.Show();
            var tabs = Assert.IsType<TabControl>(view.FindControl<TabControl>("EntralinkTabs"));
            tabs.SelectedIndex = 1;
            window.UpdateLayout();
            Assert.NotNull(view.FindControl<ListBox>("MissionList"));
            Assert.NotNull(view.FindControl<NumericUpDown>("MissionBestScoreInput"));
        }
        finally
        {
            window.Close();
        }
    }
}
