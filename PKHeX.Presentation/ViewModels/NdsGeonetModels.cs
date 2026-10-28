using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public sealed record GeonetPointOption(GeonetPoint Value, string Name);

public static class GeonetPointOptions
{
    public static IReadOnlyList<GeonetPointOption> Create() =>
    [
        new(GeonetPoint.None, LocalizedStrings.Instance["Geonet4Editor_PointNone"]),
        new(GeonetPoint.Blue, LocalizedStrings.Instance["Geonet4Editor_PointBlue"]),
        new(GeonetPoint.Yellow, LocalizedStrings.Instance["Geonet4Editor_PointYellow"]),
        new(GeonetPoint.Red, LocalizedStrings.Instance["Geonet4Editor_PointRed"]),
    ];
}

public partial class GeonetLocationRow(
    byte countryId, byte regionId, string country, string region,
    IReadOnlyList<GeonetPointOption> pointOptions,
    GeonetPointOption selectedPoint, Action<GeonetPointOption> onChanged) : ObservableObject
{
    public byte CountryId { get; } = countryId;
    public byte RegionId { get; } = regionId;
    public string Country { get; } = country;
    public string Region { get; } = region;
    public IReadOnlyList<GeonetPointOption> PointOptions { get; } = pointOptions;

    [ObservableProperty] private GeonetPointOption _selectedPoint = selectedPoint;

    partial void OnSelectedPointChanged(GeonetPointOption value) => onChanged(value);

    public void SetFromGeonet(GeonetPointOption value) => SelectedPoint = value;
}

internal sealed class NdsGeoNames(int generation)
{
    private readonly string[] _countries = Util.GetStringList($"gen{generation}_countries");
    private readonly Dictionary<byte, string[]> _regions = [];
    private readonly int _languageColumn = Math.Max(1, GeoLocation.GetLanguageIndex(GameInfo.CurrentLanguage) + 1);

    public string Country(byte id) => Name(_countries, id);

    public string Region(byte country, byte region)
    {
        if (region == 0) return string.Empty;
        if (!_regions.TryGetValue(country, out var names))
            _regions[country] = names = Util.GetStringList($"gen{generation}_sr_{country:000}");
        return Name(names, region);
    }

    private string Name(string[] rows, byte id)
    {
        if (id >= rows.Length) return id.ToString();
        var columns = rows[id].Split('\t');
        return _languageColumn < columns.Length && !string.IsNullOrWhiteSpace(columns[_languageColumn])
            ? columns[_languageColumn]
            : id.ToString();
    }
}
