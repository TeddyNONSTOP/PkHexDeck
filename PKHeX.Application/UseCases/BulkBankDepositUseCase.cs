using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>
/// Result of planning a bulk deposit into the Bank. <see cref="Deposits"/> is the subset of the
/// request that will move. <see cref="SkippedEmpty"/> counts marks that point at nothing and
/// <see cref="SkippedProtected"/> counts locked/overwrite-protected box slots (and LGPE party members
/// stored in boxes); both are skipped and reported, like a bulk move. <see cref="WouldEmptyParty"/>
/// is set — and <see cref="Deposits"/> is empty — when the deposit would leave the party empty or
/// with only Eggs, which is refused outright rather than partially executed.
/// </summary>
public readonly record struct BulkBankDepositPlan(
    IReadOnlyList<SlotLocation> Deposits,
    int RequestedCount,
    int SkippedEmpty,
    int SkippedProtected,
    bool WouldEmptyParty = false);

/// <summary>
/// Outcome of <see cref="BulkBankDepositUseCase.Execute"/>. On failure nothing changed: neither the
/// save nor the Bank keeps any part of the batch.
/// </summary>
public readonly record struct BulkBankDepositResult(bool Success, IReadOnlyList<BankEntry> Added, string? Error, BankErrorKind Kind = BankErrorKind.None);

/// <summary>
/// Moves multiple marked box/party slots into the Bank in one pass: the bulk form of the single-slot
/// "Send to Bank" action, with the same slot rules.
/// </summary>
/// <remarks>
/// <see cref="Execute"/> adds every entity to the Bank <b>before</b> clearing any slot. If a Bank
/// write fails, the entries already added are removed again and the save is untouched; if clearing
/// fails, the slots are restored and the Bank entries removed. A Pokémon is therefore never held only
/// in memory. Each entry is added through <see cref="BankHoldTracker.Deposit"/>, so it is held until the
/// save is written and removed again if the save is closed without saving. Party slots are removed in descending order so the remaining indices stay valid while
/// the party compacts. Like the single deposit this is not an undo step: undoing it would put a second
/// copy back into the save while the Bank keeps its own.
/// </remarks>
public sealed class BulkBankDepositUseCase
{
    public BulkBankDepositPlan Plan(SaveFile sav, IReadOnlyList<SlotLocation> sources)
    {
        var deposits = new List<SlotLocation>(sources.Count);
        var empty = 0;
        var protectedCount = 0;

        foreach (var source in sources.Distinct())
        {
            if (!IsOccupied(sav, source))
            {
                empty++;
                continue;
            }

            if (!source.IsParty && IsProtectedBoxSlot(sav, source))
            {
                protectedCount++;
                continue;
            }

            deposits.Add(source);
        }

        if (WouldEmptyParty(sav, deposits))
            return new BulkBankDepositPlan([], sources.Count, empty, protectedCount, WouldEmptyParty: true);

        return new BulkBankDepositPlan(deposits, sources.Count, empty, protectedCount);
    }

    /// <summary>
    /// Deposits every slot in <paramref name="deposits"/> (a plan from <see cref="Plan"/>) and clears
    /// it. <paramref name="saveLabel"/> is recorded as each entry's origin.
    /// </summary>
    public BulkBankDepositResult Execute(SaveFile sav, BankHoldTracker holds, IReadOnlyList<SlotLocation> deposits, string saveLabel, string? savePath)
    {
        ArgumentNullException.ThrowIfNull(sav);
        ArgumentNullException.ThrowIfNull(holds);

        var captured = deposits.Select(location => (Location: location, Entity: ReadSlot(sav, location))).ToList();
        var added = new List<BankEntry>(captured.Count);

        foreach (var (location, pk) in captured)
        {
            var result = holds.Deposit(sav, pk, location, saveLabel, savePath);
            if (!result.Success || result.Entry is null)
            {
                CancelAll(holds, added);
                return new BulkBankDepositResult(false, [], result.Error, result.Kind);
            }
            added.Add(result.Entry);
        }

        var party = sav.HasParty ? sav.PartyData.Select(pk => pk.Clone()).ToArray() : [];
        try
        {
            foreach (var (location, _) in captured.Where(c => !c.Location.IsParty))
                sav.SetBoxSlotAtIndex(sav.BlankPKM, location.Box, location.Slot);
            foreach (var (location, _) in captured.Where(c => c.Location.IsParty).OrderByDescending(c => c.Location.Slot))
                sav.DeletePartySlot(location.Slot);
        }
        catch (Exception ex)
        {
            RestoreSlots(sav, captured, party);
            CancelAll(holds, added);
            return new BulkBankDepositResult(false, [], ex.Message);
        }

        return new BulkBankDepositResult(true, added, null);
    }

    private static void RestoreSlots(SaveFile sav, IEnumerable<(SlotLocation Location, PKM Entity)> captured, PKM[] party)
    {
        foreach (var (location, pk) in captured.Where(c => !c.Location.IsParty))
        {
            try { sav.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None); }
            catch { /* best effort: the Bank rollback below keeps no copy, so keep restoring the rest */ }
        }

        for (var i = 0; i < party.Length; i++)
        {
            try { sav.SetPartySlotAtIndex(party[i], i, EntityImportSettings.None); }
            catch { /* best effort, as above */ }
        }
    }

    private static void CancelAll(BankHoldTracker holds, IEnumerable<BankEntry> added)
    {
        foreach (var entry in added)
            holds.CancelDeposit(entry.Id);
    }

    private static bool IsOccupied(SaveFile sav, SlotLocation location)
    {
        if (location.IsParty)
            return sav.HasParty && (uint)location.Slot < (uint)sav.PartyCount && sav.GetPartySlotAtIndex(location.Slot).Species != 0;
        return sav.HasBox && (uint)location.Box < (uint)sav.BoxCount && (uint)location.Slot < (uint)sav.BoxSlotCount
            && sav.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0;
    }

    // Same rules as the single-slot deposit: locked or overwrite-protected (battle team, starter), or an
    // LGPE party member that lives in a box slot (clearing it would break the party).
    private static bool IsProtectedBoxSlot(SaveFile sav, SlotLocation location)
    {
        if (sav.IsBoxSlotOverwriteProtected(location.Box, location.Slot))
            return true;
        var info = new SlotInfoBox(location.Box, location.Slot, sav);
        return !info.CanWriteTo(sav) || info.Type == StorageSlotType.Party;
    }

    private static bool WouldEmptyParty(SaveFile sav, IReadOnlyList<SlotLocation> deposits)
    {
        if (!sav.HasParty || sav.PartyCount == 0)
            return false;

        var removed = deposits.Where(d => d.IsParty).Select(d => d.Slot).ToHashSet();
        if (removed.Count == 0)
            return false;

        for (var i = 0; i < sav.PartyCount; i++)
        {
            if (!removed.Contains(i) && !sav.GetPartySlotAtIndex(i).IsEgg)
                return false;
        }
        return true;
    }

    private static PKM ReadSlot(SaveFile sav, SlotLocation location) => location.IsParty
        ? sav.GetPartySlotAtIndex(location.Slot)
        : sav.GetBoxSlotAtIndex(location.Box, location.Slot);
}
