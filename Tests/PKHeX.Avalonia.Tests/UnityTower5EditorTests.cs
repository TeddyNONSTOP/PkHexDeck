using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class UnityTower5EditorTests
{
    [Fact]
    public void PointFloorAndFlagsAreTransactionalAndRoundTrip()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../savefiles/gen5_white2.sav"));
        var save = Assert.IsType<SAV5B2W2>(FileUtil.GetSupportedFile(path));
        var before = save.Data.ToArray();
        var editedBefore = save.State.Edited;
        var vm = new UnityTower5EditorViewModel(save);
        var france = Assert.Single(vm.Locations, r => r.CountryId == 73 && r.RegionId == 1);
        var floor = Assert.Single(vm.Floors, r => r.CountryId == 73);
        Assert.Contains("France", france.Country, StringComparison.Ordinal);

        france.SelectedPoint = vm.PointOptions[(int)GeonetPoint.Blue];
        floor.IsUnlocked = true;
        vm.GlobalFlag = true;
        vm.UnityTowerFlag = true;
        Assert.Equal(before, save.Data.ToArray());
        Assert.Equal(editedBefore, save.State.Edited);
        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());

        vm = new UnityTower5EditorViewModel(save);
        Assert.Single(vm.Locations, r => r.CountryId == 73 && r.RegionId == 1).SelectedPoint =
            vm.PointOptions[(int)GeonetPoint.Yellow];
        Assert.Single(vm.Floors, r => r.CountryId == 73).IsUnlocked = true;
        vm.GlobalFlag = true;
        vm.UnityTowerFlag = true;
        vm.SaveCommand.Execute(null);

        Assert.True(save.UnityTower.GetUnityTowerFloor(73));

        var reloaded = new SAV5B2W2(save.Write().ToArray()).UnityTower;
        Assert.Equal(GeonetPoint.Yellow, reloaded.GetCountrySubregion(73, 1));
        Assert.True(reloaded.GetUnityTowerFloor(73));
        Assert.True(reloaded.GlobalFlag);
        Assert.True(reloaded.UnityTowerFlag);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void BulkActionsRefreshLocationsAndFloors()
    {
        var vm = new UnityTower5EditorViewModel(new SAV5B2W2());
        vm.SetAllLocationsCommand.Execute(null);
        Assert.Equal(GeonetPoint.Yellow,
            Assert.Single(vm.Locations, r => r.CountryId == 73 && r.RegionId == 1).SelectedPoint.Value);
        Assert.True(Assert.Single(vm.Floors, r => r.CountryId == 73).IsUnlocked);

        vm.ClearAllLocationsCommand.Execute(null);
        Assert.Equal(GeonetPoint.None,
            Assert.Single(vm.Locations, r => r.CountryId == 73 && r.RegionId == 1).SelectedPoint.Value);
        Assert.False(Assert.Single(vm.Floors, r => r.CountryId == 73).IsUnlocked);

        vm.SetAllLegalLocationsCommand.Execute(null);
        Assert.True(Assert.Single(vm.Floors, r => r.CountryId == 73).IsUnlocked);
        Assert.False(Assert.Single(vm.Floors, r => r.CountryId == 4).IsUnlocked);
        vm.FilterText = "France";
        Assert.Contains(vm.Locations, r => r.CountryId == 73);
        Assert.DoesNotContain(vm.Floors, r => r.CountryId == 220);
    }

    [Fact]
    public void AllThreeBulkActionsPersistOnlyTheirIntendedLocations()
    {
        var allSave = new SAV5B2W2();
        var all = new UnityTower5EditorViewModel(allSave);
        all.SetAllLocationsCommand.Execute(null);
        all.SaveCommand.Execute(null);
        Assert.Equal(GeonetPoint.Yellow, allSave.UnityTower.GetCountrySubregion(73, 1));
        Assert.True(allSave.UnityTower.GetUnityTowerFloor(4));

        var legalSave = new SAV5B2W2();
        var legal = new UnityTower5EditorViewModel(legalSave);
        legal.SetAllLegalLocationsCommand.Execute(null);
        legal.SaveCommand.Execute(null);
        Assert.True(legalSave.UnityTower.GetUnityTowerFloor(73));
        Assert.False(legalSave.UnityTower.GetUnityTowerFloor(4));

        var clear = new UnityTower5EditorViewModel(allSave);
        clear.ClearAllLocationsCommand.Execute(null);
        clear.SaveCommand.Execute(null);
        Assert.Equal(GeonetPoint.None, allSave.UnityTower.GetCountrySubregion(73, 1));
        Assert.False(allSave.UnityTower.GetUnityTowerFloor(73));
    }

    [AvaloniaFact]
    public void BothLocationAndFloorGridsAreReachable()
    {
        var view = new UnityTower5Editor
        {
            DataContext = new UnityTower5EditorViewModel(new SAV5B2W2()),
            Width = 620,
            Height = 550,
        };
        var window = new Window { Content = view, Width = 640, Height = 570 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var tabs = Assert.IsType<TabControl>(view.FindControl<TabControl>("DetailTabs"));
            Assert.NotNull(view.FindControl<DataGrid>("LocationGrid"));
            tabs.SelectedIndex = 1;
            window.UpdateLayout();
            Assert.NotNull(view.FindControl<DataGrid>("FloorGrid"));
        }
        finally
        {
            window.Close();
        }
    }
}
