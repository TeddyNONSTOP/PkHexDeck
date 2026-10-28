namespace PKHeX.Application.Models.Events;

/// <summary>One named BDSP event slot from the bundled name lists.</summary>
/// <param name="Category">Category key, matching the <c>Events_Category_*</c> strings.</param>
/// <param name="Name">The game's internal name, with the description in brackets when the list has one.</param>
public sealed record BdspEventName(string Category, string Name);

/// <summary>
/// Fuller names for BDSP event flags, system flags and work values than PKHeX.Core ships (it names about 2% of
/// them), plus Luminescent Platinum's custom flags. The lists and their licenses are described in
/// <c>Resources/EventsBdsp/NOTICE.md</c>. Core's own labels still win where they exist; these fill the rest.
/// </summary>
public static class BdspEventNames
{
    private static readonly Lazy<Dictionary<(EventDataKind, int), BdspEventName>> Bdsp = new(() => Read("bdsp_event_names.txt"));
    private static readonly Lazy<Dictionary<(EventDataKind, int), BdspEventName>> Lumi = new(() =>
    {
        var names = new Dictionary<(EventDataKind, int), BdspEventName>(Bdsp.Value);
        foreach (var (key, value) in Read("lumi_event_names.txt"))
            names[key] = value;
        return names;
    });

    /// <summary>The names for a BDSP save, or for a Luminescent Platinum save with Lumi's custom names on top.</summary>
    public static IReadOnlyDictionary<(EventDataKind Kind, int Index), BdspEventName> For(bool luminescent)
        => luminescent ? Lumi.Value : Bdsp.Value;

    private static Dictionary<(EventDataKind, int), BdspEventName> Read(string file)
    {
        using var stream = typeof(BdspEventNames).Assembly.GetManifestResourceStream($"PKHeX.Application.Resources.EventsBdsp.{file}")
            ?? throw new InvalidOperationException($"Missing BDSP event name list {file}.");
        using var reader = new StreamReader(stream);
        var names = new Dictionary<(EventDataKind, int), BdspEventName>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 4 || !int.TryParse(parts[1], out var index))
                continue;
            var kind = parts[0] switch { "F" => EventDataKind.Flag, "S" => EventDataKind.SystemFlag, _ => EventDataKind.Work };
            var detail = parts.Length > 4 ? parts[4] : string.Empty;
            var name = detail.Length == 0 ? parts[3] : $"{parts[3]} ({detail})";
            names[(kind, index)] = new BdspEventName(parts[2], name);
        }
        return names;
    }
}
