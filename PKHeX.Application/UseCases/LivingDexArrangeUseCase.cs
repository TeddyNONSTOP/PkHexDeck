using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>Choices for <see cref="LivingDexArrangeUseCase.Plan"/>.</summary>
/// <param name="StartBox">Zero-based first box to arrange. Earlier boxes and the party are never touched.</param>
/// <param name="IncludeForms">Give every form its own slot (same targets as the Living Dex tracker).</param>
/// <param name="LeaveGaps">Keep an empty slot for each missing species, so every species always sits at the same slot.</param>
/// <param name="PreferShiny">When several Pokémon could fill a slot, use a shiny one.</param>
public readonly record struct LivingDexArrangeOptions(int StartBox, bool IncludeForms, bool LeaveGaps, bool PreferShiny);

/// <summary>A Pokémon that changes slot. Boxes and slots are zero-based.</summary>
public sealed record LivingDexArrangeMove(PKM Entity, int FromBox, int FromSlot, int ToBox, int ToSlot, bool IsDexSlot);

/// <summary>One box slot whose content changes; <see cref="Entity"/> is <see langword="null"/> when it becomes empty.</summary>
public sealed record LivingDexArrangeWrite(int Box, int Slot, PKM? Entity);

/// <summary>Result of <see cref="LivingDexArrangeUseCase.Plan"/>.</summary>
public sealed record LivingDexArrangePlan(
    IReadOnlyList<LivingDexArrangeWrite> Writes,
    IReadOnlyList<LivingDexArrangeMove> Moves,
    int Placed,
    int Missing,
    int PartyOnly,
    int Extras,
    int FixedSlots,
    int SlotsNeeded,
    int SlotsAvailable)
{
    /// <summary>The arrangement does not fit in the boxes from the start box onwards.</summary>
    public bool Fits => SlotsNeeded <= SlotsAvailable;
    public int ShortBy => Math.Max(0, SlotsNeeded - SlotsAvailable);
}

/// <summary>
/// Living Dex autopilot: rearranges the Pokémon already in the boxes into national dex order, one per
/// species (or form), followed by every other Pokémon (duplicates, eggs, species from other games) in
/// their original order. Nothing is created or deleted: every Pokémon in the arranged boxes is still
/// there afterwards, byte for byte, in some slot.
/// </summary>
/// <remarks>
/// Locked and overwrite-protected box slots keep their Pokémon and are skipped as destinations. The
/// party and the boxes before <see cref="LivingDexArrangeOptions.StartBox"/> are left exactly as they are.
/// </remarks>
public sealed class LivingDexArrangeUseCase
{
    public LivingDexArrangePlan Plan(SaveFile sav, LivingDexArrangeOptions options)
    {
        var destinations = new List<(int Box, int Slot)>();
        var sources = new List<(int Box, int Slot, PKM Entity)>();
        var fixedSlots = 0;

        if (sav.HasBox)
        {
            for (var box = Math.Max(0, options.StartBox); box < sav.BoxCount; box++)
            {
                for (var slot = 0; slot < sav.BoxSlotCount; slot++)
                {
                    if (IsFixed(sav, box, slot))
                    {
                        fixedSlots++;
                        continue;
                    }
                    destinations.Add((box, slot));
                    var pk = sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species != 0)
                        sources.Add((box, slot, pk));
                }
            }
        }

        var targets = LivingDexTrackerUseCase.GetTargets(sav, options.IncludeForms);
        var targetSet = targets.ToHashSet();
        var candidates = new Dictionary<(ushort, byte), List<int>>();
        for (var i = 0; i < sources.Count; i++)
        {
            var pk = sources[i].Entity;
            if (pk.IsEgg)
                continue;
            var key = options.IncludeForms ? (pk.Species, pk.Form) : (pk.Species, (byte)0);
            if (!targetSet.Contains(key))
                key = (pk.Species, (byte)0);
            if (!targetSet.Contains(key))
                continue;
            if (!candidates.TryGetValue(key, out var list))
                candidates[key] = list = [];
            list.Add(i);
        }

        var partyKeys = ReadPartyKeys(sav, options.IncludeForms, targetSet);
        var used = new bool[sources.Count];
        var layout = new List<(int? Source, bool IsDexSlot)>();
        int placed = 0, missing = 0, partyOnly = 0;
        foreach (var target in targets)
        {
            if (candidates.TryGetValue(target, out var list))
            {
                // Shiny first when asked, otherwise the earliest slot, so repeat runs are stable.
                var pick = options.PreferShiny ? list.FirstOrDefault(i => sources[i].Entity.IsShiny, list[0]) : list[0];
                used[pick] = true;
                layout.Add((pick, true));
                placed++;
                continue;
            }

            missing++;
            if (partyKeys.Contains(target))
                partyOnly++;
            if (options.LeaveGaps)
                layout.Add((null, true));
        }

        // Trailing gaps hold nothing; dropping them means a nearly complete dex still fits.
        while (layout.Count > 0 && layout[^1].Source is null)
            layout.RemoveAt(layout.Count - 1);

        var extras = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            if (used[i])
                continue;
            layout.Add((i, false));
            extras++;
        }

        if (layout.Count > destinations.Count)
            return new LivingDexArrangePlan([], [], placed, missing, partyOnly, extras, fixedSlots, layout.Count, destinations.Count);

        var writes = new List<LivingDexArrangeWrite>();
        var moves = new List<LivingDexArrangeMove>();
        var occupied = sources.Select(s => (s.Box, s.Slot)).ToHashSet();
        for (var d = 0; d < destinations.Count; d++)
        {
            var (box, slot) = destinations[d];
            var (source, isDexSlot) = d < layout.Count ? layout[d] : (null, false);
            if (source is null)
            {
                if (occupied.Contains((box, slot)))
                    writes.Add(new LivingDexArrangeWrite(box, slot, null));
                continue;
            }

            var from = sources[source.Value];
            if (from.Box == box && from.Slot == slot)
                continue;
            writes.Add(new LivingDexArrangeWrite(box, slot, from.Entity));
            moves.Add(new LivingDexArrangeMove(from.Entity, from.Box, from.Slot, box, slot, isDexSlot));
        }

        return new LivingDexArrangePlan(writes, moves, placed, missing, partyOnly, extras, fixedSlots, layout.Count, destinations.Count);
    }

    /// <summary>
    /// Writes a plan into the save. The Pokémon are stored unchanged: no trade, handler, Pokédex or record
    /// updates are applied, since they are only changing slot within the same save.
    /// </summary>
    public void Execute(SaveFile sav, LivingDexArrangePlan plan)
    {
        if (!plan.Fits)
            throw new InvalidOperationException("The arrangement does not fit in the selected boxes.");
        foreach (var write in plan.Writes)
            sav.SetBoxSlotAtIndex(write.Entity ?? sav.BlankPKM, write.Box, write.Slot, EntityImportSettings.None);
    }

    private static bool IsFixed(SaveFile sav, int box, int slot)
        => sav.IsBoxSlotLocked(box, slot) || sav.IsBoxSlotOverwriteProtected(box, slot);

    private static HashSet<(ushort, byte)> ReadPartyKeys(SaveFile sav, bool includeForms, HashSet<(ushort, byte)> targets)
    {
        var keys = new HashSet<(ushort, byte)>();
        if (!sav.HasParty)
            return keys;
        for (var i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (pk.Species == 0 || pk.IsEgg)
                continue;
            var key = includeForms ? (pk.Species, pk.Form) : (pk.Species, (byte)0);
            keys.Add(targets.Contains(key) ? key : (pk.Species, (byte)0));
        }
        return keys;
    }
}
