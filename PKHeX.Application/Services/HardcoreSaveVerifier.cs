using System;
using System.Collections.Generic;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>
/// Hardcore mode's save check: compares a save about to be written with its last saved state
/// (the baseline) and allows the write only when the difference is made of moves.
/// </summary>
/// <remarks>
/// Two conditions, both required:
/// <list type="number">
/// <item>Every Pokémon in the boxes and party is byte-for-byte one that was in the baseline, or one
/// that arrived by a move (a Bank withdrawal), each used at most once. Edits, new Pokémon and copies
/// all fail here; Pokémon that left (released, deposited) are fine.</item>
/// <item>Everything else in the save is unchanged. The baseline is given the candidate's boxes,
/// party, current box, box names and wallpapers, and both are serialized and compared. Both sides
/// are first rewritten through the same slot writes so formats that keep stale bytes in unused
/// list entries (Gen 1) compare equal.</item>
/// </list>
/// </remarks>
public static class HardcoreSaveVerifier
{
    /// <summary>Returns <see langword="null"/> when <paramref name="candidate"/> differs from <paramref name="baseline"/> only by moves.</summary>
    public static SaveWriteRefusal? Verify(SaveFile baseline, SaveFile candidate, IEnumerable<PKM> arrivals)
    {
        try
        {
            if (baseline.GetType() != candidate.GetType())
                return new SaveWriteRefusal(SaveWriteRefusalKind.CannotVerify);

            var pool = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pk in EnumerateStored(baseline))
                Add(pool, pk.Entity);
            foreach (var pk in arrivals)
                Add(pool, pk);

            foreach (var (pk, box, slot) in EnumerateStored(candidate))
            {
                var key = Fingerprint(pk);
                if (!pool.TryGetValue(key, out var count) || count == 0)
                    return new SaveWriteRefusal(SaveWriteRefusalKind.PokemonChanged, box, slot, pk.Species);
                pool[key] = count - 1;
            }

            var expected = baseline.Clone();
            CopyStorage(candidate, expected);
            var actual = candidate.Clone();
            CopyStorage(candidate, actual);
            return expected.Write().Span.SequenceEqual(actual.Write().Span)
                ? null
                : new SaveWriteRefusal(SaveWriteRefusalKind.SaveDataChanged);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Hardcore save check failed: {ex.Message}");
            return new SaveWriteRefusal(SaveWriteRefusalKind.CannotVerify);
        }
    }

    /// <summary>
    /// Identity of a stored Pokémon: its decrypted stored bytes plus nickname and OT name (the Game Boy
    /// formats keep those outside the entity data). Party-only bytes such as current stats are excluded,
    /// so moving between box and party keeps the identity.
    /// </summary>
    public static string Fingerprint(PKM pk)
    {
        var data = pk.Data;
        var stored = data[..Math.Min(pk.SIZE_STORED, data.Length)];
        return string.Concat(
            Convert.ToHexString(stored),
            Convert.ToHexString(pk.NicknameTrash),
            Convert.ToHexString(pk.OriginalTrainerTrash));
    }

    private static void Add(Dictionary<string, int> pool, PKM pk)
    {
        var key = Fingerprint(pk);
        pool[key] = pool.GetValueOrDefault(key) + 1;
    }

    private static IEnumerable<(PKM Entity, int Box, int Slot)> EnumerateStored(SaveFile sav)
    {
        if (sav.HasBox)
        {
            for (int box = 0; box < sav.BoxCount; box++)
            {
                for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                {
                    var pk = sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species != 0)
                        yield return (pk, box, slot);
                }
            }
        }

        if (sav.HasParty)
        {
            for (int slot = 0; slot < sav.PartyCount; slot++)
            {
                var pk = sav.GetPartySlotAtIndex(slot);
                if (pk.Species != 0)
                    yield return (pk, -1, slot);
            }
        }
    }

    /// <summary>Writes the boxes, party and box layout of <paramref name="source"/> into <paramref name="target"/> without any import side effects.</summary>
    private static void CopyStorage(SaveFile source, SaveFile target)
    {
        if (source.HasBox)
        {
            for (int box = 0; box < source.BoxCount; box++)
            {
                for (int slot = 0; slot < source.BoxSlotCount; slot++)
                    target.SetBoxSlotAtIndex(source.GetBoxSlotAtIndex(box, slot), box, slot, EntityImportSettings.None);
            }

            if (source is IBoxDetailNameRead names && target is IBoxDetailName targetNames)
            {
                for (int box = 0; box < source.BoxCount; box++)
                    targetNames.SetBoxName(box, names.GetBoxName(box));
            }
            if (source is IBoxDetailWallpaper wallpapers && target is IBoxDetailWallpaper targetWallpapers)
            {
                for (int box = 0; box < source.BoxCount; box++)
                    targetWallpapers.SetBoxWallpaper(box, wallpapers.GetBoxWallpaper(box));
            }
            target.CurrentBox = source.CurrentBox;
        }

        if (source.HasParty)
        {
            // In order, so the party count follows the source: a blank slot ends the party.
            for (int slot = 0; slot < 6; slot++)
                target.SetPartySlotAtIndex(source.GetPartySlotAtIndex(slot), slot, EntityImportSettings.None);
        }
    }
}
