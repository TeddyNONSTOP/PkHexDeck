using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>
/// Tracks an in-progress multi-slot "mark" selection across the box/party grid, independent of each
/// viewer's own single-slot cursor (<c>BoxViewerViewModel.SelectedIndex</c>/<c>PartyViewerViewModel.SelectedIndex</c>).
/// Pure bookkeeping only — no <see cref="SaveFile"/> mutation happens here. Named distinctly from the
/// existing, unrelated "Batch Editor" (instruction/filter engine) and "Box Manipulation" (whole-box
/// Sort/Clear/Modify) features; this is the per-item multi-select "Organizer" layer.
/// </summary>
/// <remarks>
/// Registered as a DI singleton (see <see cref="DependencyInjection.AddApplication"/>) alongside
/// <see cref="UndoRedoService"/> so one shared instance is marked into while navigating between boxes
/// and the party panel ("mark across pages, act once"). A party slot is never produced by
/// <see cref="MarkAllInBox"/> (box-only, by construction) and bulk release always excludes party marks
/// (see <c>BulkReleaseSlotsUseCase</c>), mirroring this fork's existing single-item policy that a party
/// Pokémon cannot be deleted from the UI. Marks are positional (box/slot or party index); callers drop
/// stale ones with <see cref="Prune"/>/<see cref="UnmarkParty"/> after a write that moved entities.
/// </remarks>
public sealed class BoxSelectionService
{
    private readonly HashSet<SlotLocation> _marked = [];

    /// <summary>Raised whenever selection-mode state or the marked set changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Whether the box/party grids should show mark affordances instead of plain single-select.</summary>
    public bool IsSelectionModeEnabled { get; private set; }

    public int Count => _marked.Count;

    public IReadOnlyCollection<SlotLocation> MarkedSlots => _marked;

    public bool IsMarked(SlotLocation location) => _marked.Contains(location);

    private void Raise() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Enters selection mode. A no-op if already enabled.</summary>
    public void EnterSelectionMode()
    {
        if (IsSelectionModeEnabled)
            return;
        IsSelectionModeEnabled = true;
        Raise();
    }

    /// <summary>Leaves selection mode and drops every mark.</summary>
    public void ExitSelectionMode()
    {
        var changed = IsSelectionModeEnabled || _marked.Count != 0;
        IsSelectionModeEnabled = false;
        _marked.Clear();
        if (changed)
            Raise();
    }

    /// <summary>Toggles one slot's mark.</summary>
    public void Toggle(SlotLocation location)
    {
        if (!_marked.Remove(location))
            _marked.Add(location);
        Raise();
    }

    /// <summary>Drops every mark without leaving selection mode.</summary>
    public void Clear()
    {
        if (_marked.Count == 0)
            return;
        _marked.Clear();
        Raise();
    }

    /// <summary>Drops the given marks (e.g. the sources a bulk action has just processed).</summary>
    public void Unmark(IEnumerable<SlotLocation> locations)
    {
        var changed = false;
        foreach (var location in locations)
            changed |= _marked.Remove(location);
        if (changed)
            Raise();
    }

    /// <summary>
    /// Drops every party mark. Party marks are index-based and the party compacts when a member
    /// leaves it, so after such a write the remaining party indices no longer name the same Pokémon.
    /// </summary>
    public void UnmarkParty()
    {
        if (_marked.RemoveWhere(location => location.IsParty) != 0)
            Raise();
    }

    /// <summary>
    /// Marks every occupied slot in <paramref name="box"/>. Box-only by construction — a party cannot
    /// be marked in bulk this way, structurally ruling out "select-all empties the whole party".
    /// </summary>
    public void MarkAllInBox(SaveFile sav, int box)
    {
        if (!sav.HasBox || box < 0 || box >= sav.BoxCount)
            return;

        var changed = false;
        for (var slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            if (sav.GetBoxSlotAtIndex(box, slot).Species != 0)
                changed |= _marked.Add(SlotLocation.FromBox(box, slot));
        }

        if (changed)
            Raise();
    }

    /// <summary>
    /// Drops marks that no longer point at an occupied slot (e.g. after a bulk action moved/released
    /// them, or an undo emptied them), keeping the visible mark set honest after a partial
    /// skip-and-report bulk action.
    /// </summary>
    public void Prune(SaveFile sav)
    {
        if (_marked.RemoveWhere(location => !IsOccupied(sav, location)) != 0)
            Raise();
    }

    private static bool IsOccupied(SaveFile sav, SlotLocation location)
    {
        try
        {
            if (location.IsParty)
                return sav.HasParty && (uint)location.Slot < 6 && sav.GetPartySlotAtIndex(location.Slot).Species != 0;
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

    /// <summary>
    /// Snapshot of the current marks in a deterministic order (party first, then box by box, slot by
    /// slot), stable against later mutation of the live set. Bulk transfers fill destinations in this
    /// order, so the result does not depend on the order the user happened to click.
    /// </summary>
    public IReadOnlyList<SlotLocation> Snapshot() => _marked
        .OrderBy(location => location.IsParty ? 0 : 1)
        .ThenBy(location => location.Box)
        .ThenBy(location => location.Slot)
        .ToList();
}
