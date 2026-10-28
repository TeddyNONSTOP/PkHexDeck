using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>How a stored Pokémon came to the player, as far as the Nuzlocke report can tell.</summary>
public enum NuzlockeOrigin
{
    /// <summary>Caught by the player: counts as that location's encounter.</summary>
    Caught,

    /// <summary>Hatched from an egg; filed under where the egg was received.</summary>
    Egg,

    /// <summary>Another trainer's Pokémon (a trade): not one of the player's encounters.</summary>
    OtherTrainer,
}

/// <summary>One stored Pokémon as the Nuzlocke report sees it.</summary>
/// <param name="Box">Box index, or -1 for the party.</param>
/// <param name="Slot">Box or party slot.</param>
/// <param name="Family">Base species of its evolution family, for the dupes clause.</param>
/// <param name="Order">Storage order (party first, then boxes), the last tie-breaker for "first".</param>
public sealed record NuzlockeCatch(
    ushort Species,
    byte Form,
    string Nickname,
    bool IsNicknamed,
    byte Level,
    DateOnly? MetDate,
    byte MetLevel,
    NuzlockeOrigin Origin,
    ushort Family,
    int Box,
    int Slot,
    int Order);

/// <summary>
/// One encounter location: every catch there, earliest first. <see cref="Counted"/> is the encounter the
/// dupes clause keeps: the earliest catch whose family was not already counted at an earlier location.
/// </summary>
public sealed record NuzlockeLocation(string Name, IReadOnlyList<NuzlockeCatch> Catches, NuzlockeCatch? Counted)
{
    public NuzlockeCatch First => Catches[0];

    /// <summary>The earliest catch here is the same family as an encounter counted at an earlier location.</summary>
    public bool FirstIsDupe => !ReferenceEquals(Counted, First);

    /// <summary>Catches beyond the one that counts (a strict Nuzlocke allows one per location).</summary>
    public IEnumerable<NuzlockeCatch> Extras => Catches.Where(c => !ReferenceEquals(c, Counted));
}

/// <param name="Locations">Locations in the order their first catch happened.</param>
/// <param name="OtherTrainers">Pokémon from other trainers, which are not the player's encounters.</param>
/// <param name="EggCount">Unhatched eggs, which are not encounters yet.</param>
public sealed record NuzlockeReport(IReadOnlyList<NuzlockeLocation> Locations, IReadOnlyList<NuzlockeCatch> OtherTrainers, int EggCount);

/// <summary>
/// Nuzlocke report: groups a save's Pokémon by where they were met and works out each location's first
/// encounter, extra catches and dupes (an encounter whose evolution family was already caught earlier).
/// Read-only; never changes the save.
/// </summary>
/// <remarks>
/// "First" is by met date, then met level, then storage order (games before Gen 4 keep no met date).
/// Hatched Pokémon are filed under where the egg was received. Released or fainted Pokémon leave no
/// record in a save, so the report shows only what is still stored.
/// </remarks>
public sealed class NuzlockeReportUseCase
{
    public NuzlockeReport Build(SaveFile sav)
    {
        var caught = new List<(string Location, NuzlockeCatch Catch)>();
        var others = new List<NuzlockeCatch>();
        var eggs = 0;
        var order = 0;

        foreach (var (pk, box, slot) in ReadStored(sav))
        {
            if (pk.IsEgg)
            {
                eggs++;
                continue;
            }

            var origin = !IsPlayers(sav, pk) ? NuzlockeOrigin.OtherTrainer : pk.WasEgg ? NuzlockeOrigin.Egg : NuzlockeOrigin.Caught;
            var entry = new NuzlockeCatch(pk.Species, pk.Form, pk.Nickname, pk.IsNicknamed, pk.CurrentLevel, pk.MetDate, pk.MetLevel,
                origin, GetFamily(sav.Context, pk.Species, pk.Form), box, slot, order++);
            if (origin == NuzlockeOrigin.OtherTrainer)
            {
                others.Add(entry);
                continue;
            }

            var isEggLocation = origin == NuzlockeOrigin.Egg;
            var location = isEggLocation ? pk.EggLocation : pk.MetLocation;
            caught.Add((AreaName(GameInfo.GetLocationName(isEggLocation, location, pk.Format, pk.Generation, pk.Version)), entry));
        }

        // Earliest first catch first, so a dupe is always judged against encounters that came before it.
        var groups = caught
            .GroupBy(c => c.Location, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Catches: g.Select(c => c.Catch).OrderBy(c => c, CatchOrder.Instance).ToArray()))
            .OrderBy(g => g.Catches[0], CatchOrder.Instance)
            .ToArray();

        var countedFamilies = new HashSet<ushort>();
        var locations = new List<NuzlockeLocation>(groups.Length);
        foreach (var (name, catches) in groups)
        {
            var counted = catches.FirstOrDefault(c => !countedFamilies.Contains(c.Family));
            if (counted is not null)
                countedFamilies.Add(counted.Family);
            locations.Add(new NuzlockeLocation(name, catches, counted));
        }

        return new NuzlockeReport(locations, others, eggs);
    }

    /// <summary>
    /// Base species of the evolution family. The save's own evolution data comes first; when it knows no
    /// pre-evolution (Luminescent Platinum uses BDSP's, which stops at the Sinnoh dex) the newer games' data
    /// is tried, so families outside the game's original dex still group together.
    /// </summary>
    public static ushort GetFamily(EntityContext context, ushort species, byte form)
    {
        var base0 = EvolutionTree.GetEvolutionTree(context).GetBaseSpeciesForm(species, form).Species;
        if (base0 != species)
            return base0;
        foreach (var (fallback, max) in Fallbacks)
        {
            if (species > max)
                continue;
            var other = EvolutionTree.GetEvolutionTree(fallback).GetBaseSpeciesForm(species, form).Species;
            if (other != species)
                return other;
        }
        return species;
    }

    private static readonly (EntityContext Context, ushort MaxSpecies)[] Fallbacks =
    [
        (EntityContext.Gen9, PersonalTable.SV.MaxSpeciesID),
        (EntityContext.Gen8, PersonalTable.SWSH.MaxSpeciesID),
        (EntityContext.Gen7, PersonalTable.USUM.MaxSpeciesID),
    ];

    /// <summary>
    /// BDSP names buildings as numbered parts of their town or route ("Jubilife City (03)"); a Nuzlocke counts
    /// the area once, so the number is dropped.
    /// </summary>
    public static string AreaName(string location)
    {
        var open = location.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && location.EndsWith(')') && location.AsSpan(open + 2, location.Length - open - 3).IndexOfAnyExceptInRange('0', '9') < 0
            ? location[..open]
            : location;
    }

    private static bool IsPlayers(SaveFile sav, PKM pk)
        => pk.ID32 == sav.ID32 && string.Equals(pk.OriginalTrainerName, sav.OT, StringComparison.Ordinal);

    private static IEnumerable<(PKM Pokemon, int Box, int Slot)> ReadStored(SaveFile sav)
    {
        if (sav.HasParty)
        {
            for (var slot = 0; slot < sav.PartyCount; slot++)
            {
                var pk = sav.GetPartySlotAtIndex(slot);
                if (pk.Species != 0)
                    yield return (pk, -1, slot);
            }
        }

        if (!sav.HasBox)
            yield break;
        for (var box = 0; box < sav.BoxCount; box++)
        {
            for (var slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species != 0)
                    yield return (pk, box, slot);
            }
        }
    }

    private sealed class CatchOrder : IComparer<NuzlockeCatch>
    {
        public static readonly CatchOrder Instance = new();

        public int Compare(NuzlockeCatch? x, NuzlockeCatch? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            // A catch with a date sorts before one without; otherwise by date, met level and storage order.
            var date = (x.MetDate, y.MetDate) switch
            {
                ({ } a, { } b) => a.CompareTo(b),
                (null, { }) => 1,
                ({ }, null) => -1,
                _ => 0,
            };
            if (date != 0) return date;
            var level = x.MetLevel.CompareTo(y.MetLevel);
            return level != 0 ? level : x.Order.CompareTo(y.Order);
        }
    }
}
