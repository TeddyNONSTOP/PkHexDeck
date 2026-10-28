using System.Buffers.Binary;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

public enum PssGroupKind { Friends, Acquaintances, Passerby }

public sealed record PssContact(
    string Trainer, string Message, string Game, string Country, string Region, string FavoriteSpecies);

public sealed record PssGroup(PssGroupKind Kind, IReadOnlyList<PssContact> Contacts);

/// <summary>
/// Reads the three Gen 6 PSS contact blocks without modifying the save. Structured records
/// let the preview localize its labels without parsing Core's formatted English report.
/// Like PSS6.GetPSSParse, each group ends at a zero ID or the 100-record group limit.
/// </summary>
public sealed class ExportPss6UseCase
{
    private const int GroupSize = 0x5000;
    private const int RecordSize = 0xC8;
    private const int RecordsPerGroup = 100;

    public IReadOnlyList<PssGroup> Execute(SAV6 save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var data = save.Data;
        var offset = save.PSS;
        if (offset < 0 || (long)offset + (2L * GroupSize) + (RecordsPerGroup * RecordSize) > data.Length)
            throw new ArgumentException("The save does not contain complete Gen 6 PSS contact blocks.", nameof(save));

        var groups = new List<PssGroup>(3);
        for (var group = 0; group < 3; group++)
        {
            var contacts = new List<PssContact>();
            var start = offset + group * GroupSize;
            for (var record = 0; record < RecordsPerGroup; record++)
            {
                var entry = data.Slice(start + record * RecordSize, RecordSize);
                if (BinaryPrimitives.ReadUInt64LittleEndian(entry) == 0)
                    break;
                contacts.Add(ReadContact(entry));
            }
            groups.Add(new PssGroup((PssGroupKind)group, contacts));
        }
        return groups;
    }

    private static PssContact ReadContact(ReadOnlySpan<byte> entry)
    {
        var trainer = StringConverter6.GetString(entry.Slice(0x08, 0x1A));
        var message = StringConverter6.GetString(entry.Slice(0x22, 0x22));
        var game = (GameVersion)entry[0x5A];
        var gameName = game.IsGen6() ? GameInfo.GetVersionName(game) : game.ToString();
        var countryId = entry[0x57];
        var regionId = entry[0x56];
        string country, region;
        try
        {
            (country, region) = GeoLocation.GetCountryRegionText(countryId, regionId, GameInfo.CurrentLanguage);
        }
        catch (IndexOutOfRangeException)
        {
            country = countryId.ToString();
            region = regionId.ToString();
        }
        var speciesId = BinaryPrimitives.ReadUInt16LittleEndian(entry[0x9C..]) & 0x7FF;
        var species = speciesId < GameInfo.Strings.Species.Count
            ? GameInfo.Strings.Species[speciesId]
            : speciesId.ToString();
        return new PssContact(trainer, message, gameName, country, region, species);
    }
}
