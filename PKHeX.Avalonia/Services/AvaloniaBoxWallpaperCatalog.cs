using System.Reflection;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Avalonia.Services;

/// <summary>
/// <see cref="IBoxWallpaperCatalog"/> over the native box-wallpaper art already embedded in this
/// assembly for the save editors (<c>Assets/Images/box/&lt;game&gt;/box_wpNN&lt;game&gt;.png</c>, embedded as
/// <c>PKHeX.Avalonia.Assets.Images.box.&lt;game&gt;.*.png</c>). Ids are <c>"&lt;game&gt;:&lt;file name&gt;"</c>
/// (e.g. <c>"swsh:box_wp01swsh"</c>); group names are the games' localized titles.
/// </summary>
public sealed class AvaloniaBoxWallpaperCatalog : IBoxWallpaperCatalog
{
    private const string Prefix = "PKHeX.Avalonia.Assets.Images.box.";

    // Chronological, with the versions whose localized names title each group.
    private static readonly (string Folder, GameVersion[] Versions)[] Games =
    [
        ("rs", [GameVersion.R, GameVersion.S]),
        ("e", [GameVersion.E]),
        ("frlg", [GameVersion.FR, GameVersion.LG]),
        ("dp", [GameVersion.D, GameVersion.P]),
        ("pt", [GameVersion.Pt]),
        ("hgss", [GameVersion.HG, GameVersion.SS]),
        ("bw", [GameVersion.B, GameVersion.W]),
        ("b2w2", [GameVersion.B2, GameVersion.W2]),
        ("xy", [GameVersion.X, GameVersion.Y]),
        ("ao", [GameVersion.OR, GameVersion.AS]),
        ("swsh", [GameVersion.SW, GameVersion.SH]),
        ("bdsp", [GameVersion.BD, GameVersion.SP]),
        ("sv", [GameVersion.SL, GameVersion.VL]),
    ];

    private readonly Assembly _assembly = typeof(AvaloniaBoxWallpaperCatalog).Assembly;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _resources;

    public AvaloniaBoxWallpaperCatalog()
    {
        _resources = new Lazy<IReadOnlyDictionary<string, string>>(BuildIndex);
    }

    public IReadOnlyList<BoxWallpaperAsset> GetCatalog()
    {
        var result = new List<BoxWallpaperAsset>();
        foreach (var (folder, versions) in Games)
        {
            var ids = _resources.Value.Keys
                .Where(id => id.StartsWith(folder + ":", StringComparison.Ordinal))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
                continue;

            var group = string.Join(" / ", versions.Select(GetVersionName));
            result.AddRange(ids.Select(id => new BoxWallpaperAsset(id, group)));
        }
        return result;
    }

    public byte[]? GetImageBytes(string id)
    {
        if (string.IsNullOrEmpty(id) || !_resources.Value.TryGetValue(id, out var resourceName))
            return null;

        try
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string GetVersionName(GameVersion version)
    {
        try
        {
            var name = GameInfo.GetVersionName(version);
            return string.IsNullOrWhiteSpace(name) ? version.ToString() : name;
        }
        catch
        {
            return version.ToString();
        }
    }

    private IReadOnlyDictionary<string, string> BuildIndex()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;

            // "<game>.<file>.png"
            var rest = name[Prefix.Length..^".png".Length];
            var dot = rest.IndexOf('.');
            if (dot <= 0 || dot == rest.Length - 1)
                continue;

            var game = rest[..dot];
            var file = rest[(dot + 1)..];
            map[$"{game}:{file}"] = name;
        }
        return map;
    }
}
