using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class Geonet4EditorTests
{
    [Fact]
    public void LoadedHeartGoldSave_RoundTripsSelectedRegion()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../savefiles/gen4_heartgold.sav"));
        var save = Assert.IsType<SAV4HGSS>(FileUtil.GetSupportedFile(path));
        var vm = new Geonet4EditorViewModel(save);
        var france = Assert.Single(vm.Locations, r => r.CountryId == 71 && r.RegionId == 1);
        france.SelectedPoint = vm.PointOptions[(int)GeonetPoint.Yellow];
        vm.SaveCommand.Execute(null);

        var exported = save.Write().ToArray();
        var reloaded = new Geonet4(new SAV4HGSS(exported));
        Assert.Equal(GeonetPoint.Yellow, reloaded.GetCountrySubregion(71, 1));
    }

    [Fact]
    public void IndividualRegionEditsAreStagedUntilSave()
    {
        var save = new SAV4HGSS();
        var before = save.Data.ToArray();
        var editedBefore = save.State.Edited;
        var vm = new Geonet4EditorViewModel(save);
        var france = Assert.Single(vm.Locations, r => r.CountryId == 71 && r.RegionId == 1);
        Assert.True(vm.Locations.Count > LocaleNDS4.CountryCount);
        Assert.Contains("France", france.Country, StringComparison.Ordinal);
        Assert.StartsWith("Japan", Assert.Single(vm.Locations, r => r.CountryId == 103 && r.RegionId == 1).Country,
            StringComparison.Ordinal);

        france.SelectedPoint = vm.PointOptions[(int)GeonetPoint.Yellow];
        vm.GlobalFlag = true;
        Assert.Equal(before, save.Data.ToArray());
        Assert.Equal(editedBefore, save.State.Edited);

        vm.CancelCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());

        vm = new Geonet4EditorViewModel(save);
        france = Assert.Single(vm.Locations, r => r.CountryId == 71 && r.RegionId == 1);
        france.SelectedPoint = vm.PointOptions[(int)GeonetPoint.Blue];
        vm.GlobalFlag = true;
        vm.SaveCommand.Execute(null);

        var reopened = new Geonet4(save);
        Assert.Equal(GeonetPoint.Blue, reopened.GetCountrySubregion(71, 1));
        Assert.True(save.GeonetGlobalFlag);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void BulkActionsAndSearchKeepIndividualPointsVisible()
    {
        var vm = new Geonet4EditorViewModel(new SAV4Pt());
        vm.SetAllLegalLocationsCommand.Execute(null);
        var france = Assert.Single(vm.Locations, r => r.CountryId == 71 && r.RegionId == 1);
        Assert.Equal(GeonetPoint.Yellow, france.SelectedPoint.Value);

        vm.FilterText = france.Country;
        Assert.Contains(vm.Locations, r => r.CountryId == 71 && r.RegionId == 1);
        Assert.DoesNotContain(vm.Locations, r => r.CountryId == 220);

        vm.ClearAllLocationsCommand.Execute(null);
        Assert.Equal(GeonetPoint.None, france.SelectedPoint.Value);
    }

    [AvaloniaFact]
    public void ViewShowsSearchAndEditablePointGrid()
    {
        var view = new Geonet4Editor
        {
            DataContext = new Geonet4EditorViewModel(new SAV4HGSS()),
            Width = 620,
            Height = 500,
        };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            Assert.NotNull(view.FindControl<DataGrid>("LocationGrid"));
        }
        finally
        {
            window.Close();
        }
    }
}
