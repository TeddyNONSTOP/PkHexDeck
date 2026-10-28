namespace PKHeX.Application.Abstractions;

/// <summary>One selectable "art" wallpaper for the Bank wallpaper picker (Phase 2).</summary>
/// <param name="Id">Stable id used as a <see cref="BankWallpaper"/> <c>Value</c> (e.g. <c>"swsh:box_wp01swsh"</c>).</param>
/// <param name="GroupName">Human-readable game/group the art belongs to (e.g. "Sword/Shield").</param>
public readonly record struct BoxWallpaperAsset(string Id, string GroupName);

/// <summary>
/// Port exposing the bundled native box-art assets already shipped with the app (one PNG per
/// in-game wallpaper, per generation/game — see <c>PKHeX.Avalonia/Assets/Images/box/*</c>) so the
/// Bank's wallpaper picker can reuse real game art instead of inventing new artwork. Implemented in
/// the host (Avalonia) layer, which is the only layer that can read its own embedded resources;
/// Application/Infrastructure only see ids and bytes, never Avalonia types.
/// </summary>
public interface IBoxWallpaperCatalog
{
    /// <summary>All selectable art wallpapers, grouped by game.</summary>
    IReadOnlyList<BoxWallpaperAsset> GetCatalog();

    /// <summary>Raw PNG bytes for a catalog asset id, or <see langword="null"/> if unknown.</summary>
    byte[]? GetImageBytes(string id);
}
