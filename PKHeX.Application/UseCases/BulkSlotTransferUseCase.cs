using System.Collections.Generic;
using System.Linq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>One resolved source -&gt; destination pairing produced by <see cref="BulkSlotTransferUseCase"/>.</summary>
public readonly record struct BulkTransferEntry(SlotLocation Source, SlotLocation Destination);

/// <summary>
/// Result of planning a bulk move/copy/duplicate. <see cref="Entries"/> is the subset of the request
/// that fit. <see cref="SkippedNoRoom"/> counts marked Pokémon for which no empty, writable destination
/// slot was left (never overwrite — skip and report). <see cref="SkippedUnavailable"/> counts marks
/// that could not take part at all: an empty slot, a move source that already lives in the
/// destination area, or a move source in a locked/overwrite-protected box slot (moving it out would
/// have to blank that protected slot). <see cref="WouldEmptyParty"/> is set — and <see cref="Entries"/>
/// is empty — when a move would take every remaining member out of the party, which is refused
/// outright rather than partially executed.
/// </summary>
public readonly record struct BulkTransferResult(
    IReadOnlyList<BulkTransferEntry> Entries,
    int RequestedCount,
    int SkippedNoRoom,
    int SkippedUnavailable,
    bool WouldEmptyParty = false);

/// <summary>
/// Plans and executes a bulk move/copy/duplicate of multiple marked box/party slots into the next
/// available <b>empty</b> destination slots, in order. Never overwrites an occupied destination —
/// entities that do not fit are left in place and reported as skipped rather than aborting the whole
/// batch or prompting per item. Generalizes the single-pair <c>MainWindowViewModel.MoveSlotAsync</c>
/// plumbing (box&lt;-&gt;party, party compaction on move-out) to an arbitrary-size marked set.
/// </summary>
/// <remarks>
/// <see cref="Plan"/>/<see cref="PlanDuplicateInPlace"/> are pure (read <c>sav</c>, mutate nothing) so
/// they are unit-testable without undo/dialog/UI plumbing. <see cref="Execute"/> performs the writes
/// with no undo recording of its own — the caller wraps it in
/// <see cref="Services.UndoRedoService.ApplyBatch"/> so the whole bulk action undoes/redoes as one unit
/// and a mid-batch failure rolls every slot back.
/// </remarks>
public sealed class BulkSlotTransferUseCase
{
    private const int PartyCapacity = 6;

    /// <summary>
    /// Plans moving/copying <paramref name="sources"/> into the empty slots of one destination area (a
    /// box, or the party), filling forward from slot 0 of that area.
    /// </summary>
    public BulkTransferResult Plan(SaveFile sav, IReadOnlyList<SlotLocation> sources, SlotLocation destinationArea, bool clone)
    {
        var entries = new List<BulkTransferEntry>(sources.Count);
        var reserved = new HashSet<SlotLocation>();
        var noRoom = 0;
        var unavailable = 0;

        foreach (var source in sources)
        {
            if (!IsOccupied(sav, source)
                || (!clone && IsInArea(source, destinationArea))
                || (!clone && IsProtectedBoxSlot(sav, source)))
            {
                unavailable++;
                continue;
            }

            var pk = ReadSlot(sav, source);
            if (FindFreeSlot(sav, destinationArea, reserved, pk) is not { } destination)
            {
                noRoom++;
                continue;
            }

            entries.Add(new BulkTransferEntry(source, destination));
            reserved.Add(destination);
        }

        if (!clone && WouldEmptyParty(sav, entries))
            return new BulkTransferResult([], sources.Count, 0, 0, WouldEmptyParty: true);

        return new BulkTransferResult(entries, sources.Count, noRoom, unavailable);
    }

    /// <summary>
    /// Plans duplicating each marked slot into the next empty slot of <b>its own</b> area (the box it
    /// currently lives in, or the party), rather than one shared destination.
    /// </summary>
    public BulkTransferResult PlanDuplicateInPlace(SaveFile sav, IReadOnlyList<SlotLocation> sources)
    {
        var entries = new List<BulkTransferEntry>(sources.Count);
        var reserved = new HashSet<SlotLocation>();
        var noRoom = 0;
        var unavailable = 0;

        foreach (var source in sources)
        {
            if (!IsOccupied(sav, source))
            {
                unavailable++;
                continue;
            }

            var pk = ReadSlot(sav, source);
            if (FindFreeSlot(sav, source, reserved, pk) is not { } destination)
            {
                noRoom++;
                continue;
            }

            entries.Add(new BulkTransferEntry(source, destination));
            reserved.Add(destination);
        }

        return new BulkTransferResult(entries, sources.Count, noRoom, unavailable);
    }

    /// <summary>
    /// Every slot a plan's <see cref="Execute"/> writes, for undo capture. Core's party reversion
    /// snapshots the whole party for any party slot, so party compaction after a move-out is fully
    /// restored on undo even though only the moved members' indices are listed here.
    /// </summary>
    public static IReadOnlyList<SlotLocation> GetAffectedSlots(IReadOnlyList<BulkTransferEntry> entries) => entries
        .SelectMany(entry => new[] { entry.Destination, entry.Source })
        .Distinct()
        .ToList();

    /// <summary>
    /// Applies a previously built plan's entries. For a move (<paramref name="clone"/> is
    /// <see langword="false"/>), vacated party sources are compacted via
    /// <see cref="SaveFile.DeletePartySlot"/> from the highest slot index down so earlier removals
    /// never shift the index of a source still to be processed; vacated box sources are blanked.
    /// </summary>
    public void Execute(SaveFile sav, IReadOnlyList<BulkTransferEntry> entries, bool clone)
    {
        // Read every source before writing anything, so a destination write can never alias a later read.
        var payload = entries.Select(entry => (entry, pk: ReadSlot(sav, entry.Source).Clone())).ToList();
        foreach (var (entry, pk) in payload)
            WriteSlot(sav, entry.Destination, pk);

        if (clone)
            return;

        foreach (var slot in entries.Where(e => e.Source.IsParty).Select(e => e.Source.Slot).OrderByDescending(s => s))
            sav.DeletePartySlot(slot);

        foreach (var entry in entries.Where(e => !e.Source.IsParty))
            WriteSlot(sav, entry.Source, sav.BlankPKM);
    }

    private static bool WouldEmptyParty(SaveFile sav, IReadOnlyList<BulkTransferEntry> entries)
    {
        if (!sav.HasParty || sav.PartyCount == 0)
            return false;
        var removed = entries.Count(e => e.Source.IsParty);
        var added = entries.Count(e => e.Destination.IsParty);
        return removed > 0 && sav.PartyCount - removed + added <= 0;
    }

    private static bool IsInArea(SlotLocation location, SlotLocation area) => location.IsParty == area.IsParty
        && (area.IsParty || location.Box == area.Box);

    private static bool IsProtectedBoxSlot(SaveFile sav, SlotLocation location) => !location.IsParty
        && sav.IsBoxSlotOverwriteProtected(location.Box, location.Slot);

    private static SlotLocation? FindFreeSlot(SaveFile sav, SlotLocation area, HashSet<SlotLocation> reserved, PKM pk)
    {
        if (area.IsParty ? !sav.HasParty : !sav.HasBox || (uint)area.Box >= (uint)sav.BoxCount)
            return null;

        var capacity = area.IsParty ? PartyCapacity : sav.BoxSlotCount;
        for (var index = 0; index < capacity; index++)
        {
            var candidate = area.IsParty ? SlotLocation.FromParty(index) : SlotLocation.FromBox(area.Box, index);
            if (reserved.Contains(candidate) || IsOccupied(sav, candidate))
                continue;

            ISlotInfo info = area.IsParty ? new SlotInfoParty(index) : new SlotInfoBox(area.Box, index, sav);
            if (!info.CanWriteTo(sav) || info.CanWriteTo(sav, pk) != WriteBlockedMessage.None)
                continue;
            if (!area.IsParty && sav.IsBoxSlotOverwriteProtected(area.Box, index))
                continue;

            return candidate;
        }

        return null;
    }

    private static bool IsOccupied(SaveFile sav, SlotLocation location)
    {
        try
        {
            if (location.IsParty)
                return sav.HasParty && (uint)location.Slot < PartyCapacity && sav.GetPartySlotAtIndex(location.Slot).Species != 0;
            return sav.HasBox
                && (uint)location.Box < (uint)sav.BoxCount
                && (uint)location.Slot < (uint)sav.BoxSlotCount
                && sav.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0;
        }
        catch
        {
            return false;
        }
    }

    private static PKM ReadSlot(SaveFile sav, SlotLocation location) => location.IsParty
        ? sav.GetPartySlotAtIndex(location.Slot)
        : sav.GetBoxSlotAtIndex(location.Box, location.Slot);

    private static void WriteSlot(SaveFile sav, SlotLocation location, PKM pk)
    {
        if (location.IsParty)
            sav.SetPartySlotAtIndex(pk, location.Slot);
        else
            sav.SetBoxSlotAtIndex(pk, location.Box, location.Slot);
    }
}
