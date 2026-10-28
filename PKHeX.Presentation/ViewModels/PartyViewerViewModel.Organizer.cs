using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// The Organizer's party-side wiring — see <c>BoxViewerViewModel.Organizer.cs</c>; this mirrors it for
/// the party grid (shared by the detached PartyViewer and the inline PartyStrip). "Move/Copy marked
/// here" targets the party (filling its empty slots in order); Duplicate/Release/Export operate on
/// every mark regardless of where it lives (Release still excludes party marks — see
/// <see cref="BulkReleaseSlotsUseCase"/>). There is deliberately no "mark all" for the party.
/// </summary>
public partial class PartyViewerViewModel
{
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
    private bool CanBulkEditNow() => CanBulkActNow() && IsEditingAllowed;

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        foreach (var slot in Slots)
            slot.IsMarked = _selection?.IsMarked(SlotLocation.FromParty(slot.Slot)) ?? false;

        OnPropertyChanged(nameof(IsSelectionModeEnabled));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(MarkedCountText));
        ClearMarksCommand.NotifyCanExecuteChanged();
        BulkMoveHereCommand.NotifyCanExecuteChanged();
        BulkCopyHereCommand.NotifyCanExecuteChanged();
        BulkDuplicateCommand.NotifyCanExecuteChanged();
        BulkReleaseCommand.NotifyCanExecuteChanged();
        BulkExportCommand.NotifyCanExecuteChanged();
        BulkSendToBankCommand.NotifyCanExecuteChanged();
        BulkLegalizeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Unsubscribes from the shared (long-lived) selection service. Must be called before this viewer is discarded on a save switch.</summary>
    public void DetachSelectionEvents()
    {
        if (_selection is not null)
            _selection.SelectionChanged -= OnSelectionChanged;
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
    private void ToggleMark(PartySlotData? slot)
    {
        if (slot is null || _selection is null || !IsSessionCurrent())
            return;
        var location = SlotLocation.FromParty(slot.Slot);
        if (slot.IsEmpty && !_selection.IsMarked(location))
            return;
        _selection.Toggle(location);
    }

    [RelayCommand(CanExecute = nameof(HasMarks))]
    private void ClearMarks() => _selection?.Clear();

    [RelayCommand(CanExecute = nameof(CanBulkActNow))]
    private Task BulkMoveHere() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromParty(0), clone: false, LocalizedStrings.Instance["Tab_Party"]) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBulkEditNow))]
    private Task BulkCopyHere() => _bulkActions?.TransferMarkedAsync(SlotLocation.FromParty(0), clone: true, LocalizedStrings.Instance["Tab_Party"]) ?? Task.CompletedTask;

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
