namespace PKHeX.Presentation.Models;

/// <summary>
/// The flat colors a Bank box can use as its wallpaper (<see cref="Application.Abstractions.BankWallpaperKind.Color"/>
/// stores an index into this list). Muted mid-tones so the slot cards stay readable in both the
/// Light and Dark themes. Kept as hex strings so this layer stays framework-free; the host parses them.
/// </summary>
public static class BankWallpaperPalette
{
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#C0504D", "#D9844A", "#C9A93A", "#7E9F4A",
        "#3F9A72", "#3A9AA6", "#4A7FC0", "#6A63B8",
        "#9A5AA8", "#B85A85", "#8A7A68", "#6B7280",
    ];

    /// <summary>Hex color for a palette index, or <see langword="null"/> when out of range/unparseable.</summary>
    public static string? TryGetColor(string? value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index))
            return null;
        return index >= 0 && index < Colors.Count ? Colors[index] : null;
    }
}
