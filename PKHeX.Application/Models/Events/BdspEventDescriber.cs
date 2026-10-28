namespace PKHeX.Application.Models.Events;

/// <summary>
/// Describes a BDSP event flag or work value from its internal name when no published description exists.
/// It only decodes patterns whose meaning is settled in the Sinnoh script naming (what the prefix means, and
/// where the zone code inside the name is); anything else gets no description rather than a guess.
/// </summary>
/// <remarks>
/// Zone codes such as <c>D05R0110</c> come from <c>Resources/EventsBdsp/bdsp_zones.txt</c> (see NOTICE.md).
/// The text is English, like the internal names and the published descriptions it sits beside.
/// </remarks>
public static class BdspEventDescriber
{
    private static readonly Lazy<Dictionary<string, string>> Zones = new(ReadZones);

    // Words that recur in the internal names and have one plain meaning. Others are shown as they are.
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["GET"] = "received", ["PREZENT"] = "gift", ["PRESENT"] = "gift", ["TALK"] = "talked",
        ["CLEAR"] = "cleared", ["LEGEND"] = "legendary", ["BTL"] = "battle", ["WIN"] = "won",
        ["ITEM"] = "item", ["POKE"] = "Pokémon", ["SEE"] = "seen", ["START"] = "start", ["END"] = "end",
        ["FIRST"] = "first time", ["EVENT"] = "event", ["GINGA"] = "Team Galactic", ["SEKIZOU"] = "statue",
        ["KINOMI"] = "berry", ["CHAMP"] = "Champion", ["RIVAL"] = "rival",
    };

    /// <summary>A short description of <paramref name="name"/>, or <see langword="null"/> when it can't be decoded.</summary>
    public static string? Describe(string name)
    {
        if (name.StartsWith("LOCALWORK", StringComparison.Ordinal) || name.StartsWith("SCWK_", StringComparison.Ordinal)
            || name.StartsWith("UNION_TEMP", StringComparison.Ordinal) || name.StartsWith("CON_TEMP", StringComparison.Ordinal))
            return "Temporary script value; the game reuses it";

        var parts = name.Split('_');
        var zoneAt = Array.FindIndex(parts, p => Zones.Value.ContainsKey(p));
        if (zoneAt < 0)
            return null;

        var place = Zones.Value[parts[zoneAt]];
        var rest = string.Join(", ", parts.Skip(zoneAt + 1).Where(p => p.Length != 0)
            .Select(Translate));
        var prefix = string.Join("_", parts.Take(zoneAt));
        var text = prefix switch
        {
            "FLAG_ARRIVE" => $"Visited {place}",
            "WK_SCENE" => $"Story progress at {place}",
            "FV" => $"Hides a person or object at {place} when set",
            "FE" => $"Event at {place}",
            _ => $"Linked to {place}",
        };
        return rest.Length == 0 ? text : $"{text}: {rest}";
    }

    // "KINOMI3" reads as "berry 3": the word table is matched without a trailing number.
    private static string Translate(string part)
    {
        var word = part.TrimEnd("0123456789".ToCharArray());
        if (word.Length != 0 && Words.TryGetValue(word, out var plain))
            return word.Length == part.Length ? plain : $"{plain} {part[word.Length..]}";
        return part.ToLowerInvariant();
    }

    private static Dictionary<string, string> ReadZones()
    {
        using var stream = typeof(BdspEventDescriber).Assembly.GetManifestResourceStream("PKHeX.Application.Resources.EventsBdsp.bdsp_zones.txt")
            ?? throw new InvalidOperationException("Missing BDSP zone list.");
        using var reader = new StreamReader(stream);
        var zones = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var tab = line.IndexOf('\t');
            // A code without a number ("UG") is also an ordinary word in names; unused zones describe nothing.
            if (tab > 0 && line.AsSpan(0, tab).IndexOfAnyInRange('0', '9') >= 0 && !line.EndsWith("(Unused)", StringComparison.Ordinal))
                zones.TryAdd(line[..tab], line[(tab + 1)..]);
        }
        return zones;
    }
}
