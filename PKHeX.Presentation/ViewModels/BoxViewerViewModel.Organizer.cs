using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// The Organizer: per-item multi-select (mark individual slots, independent of the whole-box "Box
/// Manipulation" feature and the instruction-based "Batch Editor") plus bulk Move/Copy/Duplicate/
/// Release/Export over the marked set. This file only wires <see cref="BoxSelectionService"/> and
/// <see cref="BulkSlotActionsController"/> into the Box viewer — the orchestration lives in the
/// controller. While selection mode is on, a plain slot click toggles that slot's mark instead of only
/// moving the cursor (see <see cref="SelectSlotByClick"/>), and Enter/Space toggles the cursor slot.
/// </summary>
public partial class BoxViewerViewModel
{
    /// <summary>Exposed for the view to bind mark-mode visuals/commands; <see langword="null"/> when this viewer was constructed without Organizer support (e.g. older tests).</summary>
    public BoxSelectionService? Selection => _selection;

    /// <summary>Whether this viewer offers the Organizer at all (a selection service and a bulk controller were supplied).</summary>
    public bool SupportsOrganizer => _selection is not null && _bulkActions is not null;

    /// <summary>Whether the Actions menu offers "Send marked to Bank" (the bulk controller has a Bank).</summary>
    public bool CanBulkSendToBank => _bulkActions?.HasBank == true;

    /// <summary>Whether the Actions menu offers "Legalize marked" (the bulk controller has Auto Legality Mod).</summary>
    public bool CanBulkLegalize => _bulkActions?.HasLegalize == true;

    public bool IsSelectionModeEnabled => _selection?.IsSelectionModeEnabled ?? false;
    public int MarkedCount => _selection?.Count ?? 0;
    public bool HasMarks => MarkedCount > 0;
    public string MarkedCountText => LocalizedStrings.Instance.Format("Organizer_MarkedCount", MarkedCount);

    private bool CanBulkActNow() => _bulkActions is not null && HasMarks && IsSessionCurrent();
    private bool CanBulkActToParty() => CanBulkActNow() && _sav.HasParty;
    private bool CanBulkEditNow() => CanBulkActNow() && IsEditingAllowed;
    private bool CanBulkCopyToParty() => CanBulkActToParty() && IsEditingAllowed;
    private bool CanMarkNow() => _selection is not null && IsSelectionModeEnabled;

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        foreach (var slot in Slots)
            slot.IsMarked = _selection?.IsMarked(SlotLocation.FromBox(CurrentBox, slot.Slot)) ?? false;

        OnPropertyChanged(nameof(IsSelectionModeEnabled));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(MarkedCountText));
        SelectAllInBoxCommand.NotifyCanExecuteChanged();
        ClearMarksCommand.NotifyCanExecuteChanged();
        BulkMoveHereCommand.NotifyCanExecuteChanged();
        BulkCopyHereCommand.NotifyCanExecuteChanged();
        BulkMoveToPartyCommand.NotifyCanExecuteChanged();
        BulkCopyToPartyCommand.NotifyCanExecuteChanged();
        BulkDuplicateCommand.NotifyCanExecuteChanged();
        BulkReleaseCommand.NotifyCanExecuteChanged();
        BulkExportCommand.NotifyCanExecuteChanged();
        BulkSendToBankCommand.NotifyCanExecuteChanged();
        BulkLegalizeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Unsubscribes from the shared (long-lived) selection service. Must be called before this viewer is discarded on a save switch, otherwise the singleton keeps it alive.</summary>
    public void DetachSelectionEvents()
    {
        if (_selection is not null)
            _selection.SelectionChanged -= OnSelectionChanged;
        _detachedViewer?.DetachSelectionEvents();
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        if (_selection is null || !IsSessionCurrent())
            return;
        if (_selection.IsSelectionModeEnabled)
            _selection.ExitSelectionMode();
        else
            _selection.EnterSelectionMode();
    }

    /// <summary>Toggles one slot's mark. Empty slots cannot be marked (an existing mark can still be removed).</summary>
    [RelayCommand]
    private void ToggleMark(SlotData? slot)
    {
        if (slot is null || _selection is null || !IsSessionCurrent())
            return;
        var location = SlotLocation.FromBox(CurrentBox, slot.Slot);
        if (slot.IsEmpty && !_selection.IsMarked(location))
            return;
        _selection.Toggle(location);
    }

    [RelayCommand(CanExecute = nameof(CanMarkNow))]
    private void SelectAllInBox() => _selection?.MarkAllInBox(_sav, CurrentBox);

    [RelayCommand(CanExecute = nameof(HasMarks))]
    private void ClearMarks() => _selection?.Clear();

    [RelayCommand(CanExecute = nameof(CanBulkActNow))]
    private Task BulkMoveHere() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromBox(CurrentBox, 0), clone: false, BoxName) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkEditNow))]
    private Task BulkCopyHere() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromBox(CurrentBox, 0), clone: true, BoxName) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkActToParty))]
    private Task BulkMoveToParty() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromParty(0), clone: false, LocalizedStrings.Instance["Tab_Party"]) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkCopyToParty))]
    private Task BulkCopyToParty() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromParty(0), clone: true, LocalizedStrings.Instance["Tab_Party"]) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkEditNow))]
    private Task BulkDuplicate() => _bulkActions?.DuplicateMarkedAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkActNow))]
    private Task BulkRelease() => _bulkActions?.ReleaseMarkedAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkActNow))]
    private Task BulkExport() => _bulkActions?.ExportMarkedAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkActNow))]
    private Task BulkSendToBank() => _bulkActions?.SendMarkedToBankAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkEditNow))]
    private Task BulkLegalize() => _bulkActions?.LegalizeMarkedAsync() ?? Task.CompletedTask;
}
