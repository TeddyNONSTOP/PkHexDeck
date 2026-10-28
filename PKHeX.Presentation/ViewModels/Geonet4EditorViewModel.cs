using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

public partial class Geonet4EditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SAV4? _source;
    private readonly SAV4? _working;
    private readonly Geonet4? _geonet;
    private readonly List<GeonetLocationRow> _allLocations = [];
    private readonly NdsGeoNames _names = new(4);

    public Action? CloseRequested { get; set; }
    public bool IsSupported => _geonet is not null;
    public ObservableCollection<GeonetLocationRow> Locations { get; } = [];
    public IReadOnlyList<GeonetPointOption> PointOptions { get; } = GeonetPointOptions.Create();

    [ObservableProperty] private bool _globalFlag;
    [ObservableProperty] private string _filterText = string.Empty;

    public Geonet4EditorViewModel(SaveFile sav)
    {
        _source = sav as SAV4;
        if (_source is null)
            return;

        // Geonet4.GlobalFlag writes its SAV4 immediately. Keep all edits on a clone so Cancel
        // leaves the live save byte-for-byte unchanged, including bulk actions and the flag.
        var editedBeforeClone = _source.State.Edited;
        _working = (SAV4)_source.Clone();
        _source.State.Edited = editedBeforeClone;
        _geonet = new Geonet4(_working);
        _globalFlag = _geonet.GlobalFlag;

        for (byte country = 1; country <= LocaleNDS4.CountryCount; country++)
        {
            var regionCount = Geonet4.GetSubregionCount(country);
            if (regionCount == 0)
                AddLocation(country, 0);
            else
            {
                for (byte region = 1; region <= regionCount; region++)
                    AddLocation(country, region);
            }
        }
        RefreshFilter();
    }

    private void AddLocation(byte country, byte region)
    {
        // The 3DS GeoLocation table uses different country IDs. Read the Gen 4 NDS resources
        // used by the upstream Geonet editor so an ID never displays another country's name.
        var countryName = _names.Country(country);
        var regionName = _names.Region(country, region);
        var point = _geonet!.GetCountrySubregion(country, region);
        _allLocations.Add(new GeonetLocationRow(country, region,
            $"{countryName} ({country})", regionName,
            PointOptions, PointOptions[(int)point], option => _geonet.SetCountrySubregion(country, region, option.Value)));
    }

    partial void OnFilterTextChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        Locations.Clear();
        foreach (var row in _allLocations)
        {
            if (string.IsNullOrWhiteSpace(FilterText)
                || row.Country.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Region.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase))
                Locations.Add(row);
        }
    }

    partial void OnGlobalFlagChanged(bool value)
    {
        if (_geonet is not null)
            _geonet.GlobalFlag = value;
    }

    private void RefreshPoints()
    {
        foreach (var row in _allLocations)
            row.SetFromGeonet(PointOptions[(int)_geonet!.GetCountrySubregion(row.CountryId, row.RegionId)]);
        GlobalFlag = _geonet!.GlobalFlag;
    }

    [RelayCommand]
    private void SetAllLocations()
    {
        _geonet?.SetAll();
        if (_geonet is not null) RefreshPoints();
    }

    [RelayCommand]
    private void SetAllLegalLocations()
    {
        _geonet?.SetAllLegal();
        if (_geonet is not null) RefreshPoints();
    }

    [RelayCommand]
    private void ClearAllLocations()
    {
        _geonet?.ClearAll();
        if (_geonet is not null) RefreshPoints();
    }

    [RelayCommand]
    private void Save()
    {
        if (_source is null || _working is null || _geonet is null) return;
        _geonet.Save();
        var length = LocaleNDS4.CountryCount * 16;
        var offset = _source.Geonet + 3;
        var changed = _source.GeonetGlobalFlag != _working.GeonetGlobalFlag
            || !_source.General.Slice(offset, length).SequenceEqual(_working.General.Slice(offset, length));
        if (!changed)
        {
            CloseRequested?.Invoke();
            return;
        }
        _source.GeonetGlobalFlag = _working.GeonetGlobalFlag;
        _source.SetData(_source.General.Slice(offset, length), _working.General.Slice(offset, length));
        _source.State.Edited = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
