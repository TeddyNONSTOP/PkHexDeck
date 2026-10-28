using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>One marked Pokémon to legalize, captured with the exact stored bytes it had when planned.</summary>
public sealed record BulkLegalizeCandidate(SlotLocation Location, PKM Entity, byte[] StoredData);

/// <summary>
/// Result of planning a bulk legalization. <see cref="Candidates"/> are the illegal marked Pokémon;
/// <see cref="AlreadyLegal"/> are left untouched; <see cref="Unavailable"/> counts empty marks and
/// locked/overwrite-protected box slots (and LGPE party members stored in boxes), skipped and reported.
/// </summary>
public readonly record struct BulkLegalizePlan(
    IReadOnlyList<BulkLegalizeCandidate> Candidates,
    IReadOnlyList<SlotLocation> AlreadyLegal,
    int Unavailable);

/// <summary>Outcome of legalizing one candidate (computed off the UI thread, before anything is written).</summary>
public sealed record BulkLegalizeOutcome(BulkLegalizeCandidate Candidate, PKM? Legalized, string? Failure);

/// <summary>
/// The Organizer's bulk form of the slot "Legalize" action: rebuilds every illegal marked Pokémon in
/// place with Auto Legality Mod (<see cref="IAutoLegalityService.TryLegalizeExisting"/>), leaving legal
/// ones alone.
/// </summary>
/// <remarks>
/// Three steps so the slow part never touches the save: <see cref="Plan"/> (UI thread, pure),
/// <see cref="Legalize"/> (any thread; reads only the captured clones and the save's trainer data,
/// like the single-slot action), then <see cref="Execute"/> (UI thread) writes only the slots whose
/// stored bytes are still exactly what was planned, so an edit made while the engine ran is never
/// overwritten. The caller wraps <see cref="Execute"/> in one undo transaction.
/// </remarks>
public sealed class BulkLegalizeUseCase
{
    public BulkLegalizePlan Plan(SaveFile sav, IReadOnlyList<SlotLocation> sources)
    {
        var candidates = new List<BulkLegalizeCandidate>();
        var alreadyLegal = new List<SlotLocation>();
        var unavailable = 0;

        foreach (var source in sources.Distinct())
        {
            var pk = TryRead(sav, source);
            if (pk is null || pk.Species == 0 || IsProtected(sav, source))
            {
                unavailable++;
                continue;
            }

            if (new LegalityAnalysis(pk).Valid)
            {
                alreadyLegal.Add(source);
                continue;
            }

            candidates.Add(new BulkLegalizeCandidate(source, pk.Clone(), GetStoredData(pk)));
        }

        return new BulkLegalizePlan(candidates, alreadyLegal, unavailable);
    }

    /// <summary>Runs the engine over every candidate. CPU-bound and slow; never writes to the save.</summary>
    public IReadOnlyList<BulkLegalizeOutcome> Legalize(IAutoLegalityService service, SaveFile sav, IReadOnlyList<BulkLegalizeCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(service);
        var outcomes = new List<BulkLegalizeOutcome>(candidates.Count);
        foreach (var candidate in candidates)
        {
            try
            {
                var result = service.TryLegalizeExisting(sav, candidate.Entity.Clone());
                outcomes.Add(result is { Success: true, Pokemon: { } legal }
                    ? new BulkLegalizeOutcome(candidate, legal, null)
                    : new BulkLegalizeOutcome(candidate, null, result.Messages.Count > 0 ? result.MessageText : null));
            }
            catch (Exception ex)
            {
                outcomes.Add(new BulkLegalizeOutcome(candidate, null, ex.Message));
            }
        }
        return outcomes;
    }

    /// <summary>The successful outcomes whose slot still holds exactly the planned bytes.</summary>
    public IReadOnlyList<BulkLegalizeOutcome> Writable(SaveFile sav, IEnumerable<BulkLegalizeOutcome> outcomes) =>
        outcomes.Where(o => o.Legalized is not null
                && TryRead(sav, o.Candidate.Location) is { } current
                && GetStoredData(current).AsSpan().SequenceEqual(o.Candidate.StoredData))
            .ToList();

    /// <summary>Writes the legalized Pokémon. No undo recording of its own: the caller wraps it in one batch.</summary>
    public void Execute(SaveFile sav, IReadOnlyList<BulkLegalizeOutcome> writable)
    {
        foreach (var outcome in writable)
        {
            var location = outcome.Candidate.Location;
            if (location.IsParty)
                sav.SetPartySlotAtIndex(outcome.Legalized!, location.Slot);
            else
                sav.SetBoxSlotAtIndex(outcome.Legalized!, location.Box, location.Slot);
        }
    }

    private static bool IsProtected(SaveFile sav, SlotLocation location)
    {
        if (location.IsParty)
            return false;
        if (sav.IsBoxSlotOverwriteProtected(location.Box, location.Slot))
            return true;
        var info = new SlotInfoBox(location.Box, location.Slot, sav);
        return !info.CanWriteTo(sav) || info.Type == StorageSlotType.Party;
    }

    private static PKM? TryRead(SaveFile sav, SlotLocation location)
    {
        if (location.IsParty)
            return sav.HasParty && (uint)location.Slot < (uint)sav.PartyCount ? sav.GetPartySlotAtIndex(location.Slot) : null;
        return sav.HasBox && (uint)location.Box < (uint)sav.BoxCount && (uint)location.Slot < (uint)sav.BoxSlotCount
            ? sav.GetBoxSlotAtIndex(location.Box, location.Slot)
            : null;
    }

    private static byte[] GetStoredData(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }
}
