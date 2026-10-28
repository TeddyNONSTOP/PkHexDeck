using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

public partial class UnityTower5EditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SAV5? _source;
    private readonly SAV5? _working;
    private readonly UnityTower5? _tower;
    private readonly NdsGeoNames _names = new(5);
    private readonly List<GeonetLocationRow> _allLocations = [];
    private readonly List<UnityTowerFloorRow> _allFloors = [];

    public Action? CloseRequested { get; set; }
    public bool IsSupported => _tower is not null;
    public IReadOnlyList<GeonetPointOption> PointOptions { get; } = GeonetPointOptions.Create();
    public ObservableCollection<GeonetLocationRow> Locations { get; } = [];
    public ObservableCollection<UnityTowerFloorRow> Floors { get; } = [];

    [ObservableProperty] private bool _globalFlag;
    [ObservableProperty] private bool _unityTowerFlag;
    [ObservableProperty] private string _filterText = string.Empty;

    public UnityTower5EditorViewModel(SaveFile sav)
    {
        _source = sav as SAV5;
        if (_source is null) return;

        var editedBeforeClone = _source.State.Edited;
        _working = (SAV5)_source.Clone();
        _source.State.Edited = editedBeforeClone;
        _tower = _working.UnityTower;
        _globalFlag = _tower.GlobalFlag;
        _unityTowerFlag = _tower.UnityTowerFlag;

        for (byte country = 1; country <= LocaleNDS5.CountryCount; country++)
        {
            var countryId = country;
            var countryName = $"{_names.Country(country)} ({country})";
            _allFloors.Add(new UnityTowerFloorRow(country, countryName,
                _tower.GetUnityTowerFloor(country), unlocked => _tower.SetUnityTowerFloor(countryId, unlocked)));

            var regionCount = UnityTower5.GetSubregionCount(country);
            if (regionCount == 0)
                AddLocation(country, 0, countryName);
            else
                for (byte region = 1; region <= regionCount; region++)
                    AddLocation(country, region, countryName);
        }
        RefreshFilter();
    }

    private void AddLocation(byte country, byte region, string countryName)
    {
        var point = _tower!.GetCountrySubregion(country, region);
        _allLocations.Add(new GeonetLocationRow(country, region, countryName,
            _names.Region(country, region), PointOptions, PointOptions[(int)point],
            option => _tower.SetCountrySubregion(country, region, option.Value)));
    }

    partial void OnFilterTextChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        Locations.Clear();
        Floors.Clear();
        foreach (var row in _allLocations)
            if (string.IsNullOrWhiteSpace(FilterText)
                || row.Country.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Region.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase))
                Locations.Add(row);
        foreach (var row in _allFloors)
            if (string.IsNullOrWhiteSpace(FilterText)
                || row.Country.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase))
                Floors.Add(row);
    }

    partial void OnGlobalFlagChanged(bool value)
    {
        if (_tower is not null) _tower.GlobalFlag = value;
    }

    partial void OnUnityTowerFlagChanged(bool value)
    {
        if (_tower is not null) _tower.UnityTowerFlag = value;
    }

    private void RefreshFromTower()
    {
        foreach (var row in _allLocations)
            row.SetFromGeonet(PointOptions[(int)_tower!.GetCountrySubregion(row.CountryId, row.RegionId)]);
        foreach (var row in _allFloors)
            row.SetFromTower(_tower!.GetUnityTowerFloor(row.CountryId));
        GlobalFlag = _tower!.GlobalFlag;
        UnityTowerFlag = _tower.UnityTowerFlag;
    }

    [RelayCommand]
    private void SetAllLocations()
    {
        _tower?.SetAll();
        if (_tower is not null) RefreshFromTower();
    }

    [RelayCommand]
    private void SetAllLegalLocations()
    {
        _tower?.SetAllLegal();
        if (_tower is not null) RefreshFromTower();
    }

    [RelayCommand]
    private void ClearAllLocations()
    {
        _tower?.ClearAll();
        if (_tower is not null) RefreshFromTower();
    }

    [RelayCommand]
    private void Save()
    {
        if (_source is null || _working is null) return;
        var target = _source.UnityTower.Data;
        var staged = _working.UnityTower.Data;
        if (!target.SequenceEqual(staged))
        {
            _source.SetData(target, staged);
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}

public partial class UnityTowerFloorRow(byte countryId, string country, bool isUnlocked, Action<bool> onChanged) : ObservableObject
{
    public byte CountryId { get; } = countryId;
    public string Country { get; } = country;
    [ObservableProperty] private bool _isUnlocked = isUnlocked;
    partial void OnIsUnlockedChanged(bool value) => onChanged(value);
    public void SetFromTower(bool value) => IsUnlocked = value;
}
