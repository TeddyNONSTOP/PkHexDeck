using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>Where a Pokémon counted by the Living Dex tracker is stored.</summary>
public enum LivingDexSourceKind
{
    Party,
    Box,
    Bank,
}

/// <summary>
/// One stored Pokémon as the Living Dex tracker sees it. <see cref="Box"/> is unused for the party;
/// <see cref="Slot"/> is the zero-based party slot, box slot or Bank slot.
/// </summary>
public readonly record struct LivingDexHolding(
    ushort Species,
    byte Form,
    bool IsShiny,
    bool IsEgg,
    LivingDexSourceKind Source,
    int Box,
    int Slot);

/// <summary>One species (or species form) the save's game can hold, with every stored Pokémon that fills it.</summary>
public sealed class LivingDexTrackerEntry(ushort species, byte form, IReadOnlyList<LivingDexHolding> holdings)
{
    public ushort Species { get; } = species;
    public byte Form { get; } = form;
    public IReadOnlyList<LivingDexHolding> Holdings { get; } = holdings;

    public int Count => Holdings.Count;
    public int ShinyCount => Holdings.Count(h => h.IsShiny);
    public bool IsOwned => Holdings.Count > 0;
    public bool HasShiny => Holdings.Any(h => h.IsShiny);
}

/// <summary>Result of <see cref="LivingDexTrackerUseCase.Build"/>.</summary>
/// <param name="Entries">Every target in national dex order, then form order.</param>
/// <param name="EggCount">Eggs found in the sources. They fill no entry until they hatch.</param>
public sealed record LivingDexTrackerReport(IReadOnlyList<LivingDexTrackerEntry> Entries, int EggCount)
{
    public int Total => Entries.Count;
    public int OwnedCount => Entries.Count(e => e.IsOwned);
    public int ShinyCount => Entries.Count(e => e.HasShiny);
}

/// <summary>
/// Living Dex tracker: compares what a save (and optionally the Bank) holds against every species, or
/// every species form, obtainable in the save's game. Read-only; never changes the save.
/// </summary>
/// <remarks>
/// The targets use the same rules as the Living Dex generator (<c>LivingDexService</c>), so the
/// tracker's "missing" list is what the generator would fill: species present in the game, and in
/// form mode every stored form except lord, battle-only, fused and (outside Gen 7) totem forms.
/// </remarks>
public sealed class LivingDexTrackerUseCase
{
    public LivingDexTrackerReport Build(SaveFile sav, bool includeForms, IEnumerable<LivingDexHolding> holdings)
    {
        var targets = GetTargets(sav, includeForms);
        var buckets = new Dictionary<(ushort Species, byte Form), List<LivingDexHolding>>(targets.Count);
        foreach (var target in targets)
            buckets[target] = [];

        var eggs = 0;
        foreach (var holding in holdings)
        {
            if (holding.IsEgg)
            {
                eggs++;
                continue;
            }

            // Species mode counts any form. In form mode a stored form that is not a target (a fused
            // Kyurem, or a form from another game held in the Bank) still fills its species' base form.
            var key = includeForms ? (holding.Species, holding.Form) : (holding.Species, (byte)0);
            if (!buckets.TryGetValue(key, out var bucket) && !buckets.TryGetValue((holding.Species, 0), out bucket))
                continue;
            bucket.Add(holding);
        }

        var entries = targets.Select(t => new LivingDexTrackerEntry(t.Species, t.Form, buckets[t])).ToArray();
        return new LivingDexTrackerReport(entries, eggs);
    }

    /// <summary>The species (form 0), or species forms, the save's game can hold, in national dex order.</summary>
    public static IReadOnlyList<(ushort Species, byte Form)> GetTargets(SaveFile sav, bool includeForms)
    {
        var personal = sav.Personal;
        var context = sav.Context;
        var generation = sav.Generation;
        var strings = GameInfo.Strings;
        var targets = new List<(ushort, byte)>();

        for (ushort species = 1; species <= personal.MaxSpeciesID; species++)
        {
            if (!personal.IsSpeciesInGame(species))
                continue;
            if (!includeForms)
            {
                targets.Add((species, 0));
                continue;
            }

            var formCount = personal[species].FormCount;
            if (formCount == 1)
                formCount = (byte)FormConverter.GetFormList(species, strings.types, strings.forms, GameInfo.GenderSymbolUnicode, context).Length;

            var added = false;
            for (byte form = 0; form < formCount; form++)
            {
                if (!personal.IsPresentInGame(species, form)
                    || FormInfo.IsLordForm(species, form, context)
                    || FormInfo.IsBattleOnlyForm(species, form, generation)
                    || FormInfo.IsFusedForm(species, form, generation)
                    || (FormInfo.IsTotemForm(species, form) && context is not EntityContext.Gen7))
                    continue;
                targets.Add((species, form));
                added = true;
            }

            // Every species keeps at least its base form, so it can never vanish from the list.
            if (!added)
                targets.Add((species, 0));
        }

        return targets;
    }

    /// <summary>Every occupied party and box slot of <paramref name="sav"/>.</summary>
    public static IEnumerable<LivingDexHolding> ReadSave(SaveFile sav)
    {
        if (sav.HasParty)
        {
            for (var slot = 0; slot < sav.PartyCount; slot++)
            {
                var pk = sav.GetPartySlotAtIndex(slot);
                if (pk.Species != 0)
                    yield return new LivingDexHolding(pk.Species, pk.Form, pk.IsShiny, pk.IsEgg, LivingDexSourceKind.Party, 0, slot);
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
                    yield return new LivingDexHolding(pk.Species, pk.Form, pk.IsShiny, pk.IsEgg, LivingDexSourceKind.Box, box, slot);
            }
        }
    }

    /// <summary>
    /// Every Bank entry, except ones being withdrawn into an open save: those are already counted in
    /// that save's slot.
    /// </summary>
    public static IEnumerable<LivingDexHolding> ReadBank(IBankService bank)
        => bank.GetAll()
            .Where(e => !e.IsPendingWithdrawal)
            .Select(e => new LivingDexHolding(e.Info.Species, e.Info.Form, e.Info.IsShiny, e.Info.IsEgg, LivingDexSourceKind.Bank, e.Box, e.Slot));
}
