using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.Localization;
using PKHeX.Core;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PKHeX.Presentation.ViewModels;

public partial class PartyViewerViewModel : ViewModelBase
{
    private readonly SaveFile _sav;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly ISlotService? _slotService;
    private readonly Guid _sessionId;
    private readonly IWindowService? _windowService;
    private readonly IDialogService? _dialogService;
    private readonly IAutoLegalityService? _autoLegalityService;
    private readonly Func<int> _getCurrentBox;
    private readonly bool _haXMode;
    private readonly HardcoreMode? _hardcore;
    private readonly BoxSelectionService? _selection;
    private readonly BulkSlotActionsController? _bulkActions;
    private bool _isLegalizingSlot;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private ObservableCollection<PartySlotData> _slots = [];

    /// <summary>
    /// The slot under the current keyboard/selection cursor, used to drive
    /// keyboard-only Ctrl+C/Ctrl+V/Delete slot operations (no mouse required).
    /// </summary>
    public PartySlotData? SelectedSlot => SelectedIndex >= 0 && SelectedIndex < Slots.Count ? Slots[SelectedIndex] : null;

    public event Action<int>? SlotActivated;
    public event Action<int>? ViewSlotRequested;
    public event Action<int>? SetSlotRequested;
    public event Action<int>? DeleteSlotRequested;
    public bool CanMoveToBox => _sav.HasBox;

    /// <summary>
    /// False while Hardcore mode is on: Set, Legalize, copies and file imports are closed, while
    /// viewing, moving, releasing and Bank deposits stay available.
    /// </summary>
    public bool IsEditingAllowed => _hardcore?.Allows(SaveAction.EditPokemon) ?? true;

    /// <summary>Re-reads <see cref="IsEditingAllowed"/> after Hardcore mode was switched.</summary>
    public void RefreshHardcore()
    {
        OnPropertyChanged(nameof(IsEditingAllowed));
        BulkCopyHereCommand.NotifyCanExecuteChanged();
        BulkDuplicateCommand.NotifyCanExecuteChanged();
        BulkLegalizeCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> AllowsOrExplainAsync(SaveAction action)
    {
        if (_hardcore?.Allows(action) ?? true)
            return true;
        if (_dialogService is not null)
            await _dialogService.ShowErrorAsync(LocalizedStrings.Instance["Hardcore_Title"], LocalizedStrings.Instance["Hardcore_Blocked"]);
        return false;
    }
    public bool HasParty => _sav.HasParty;
    /// <summary>
    /// The immutable save-session identity captured when this viewer was created. A viewer from a
    /// previous save must keep its original token so its detached drag payloads cannot become valid
    /// again after the shared slot service starts a new session.
    /// </summary>
    public Guid SessionId => _sessionId;

    public PartyViewerViewModel(SaveFile sav, ISpriteRenderer spriteRenderer, ISlotService? slotService = null, IDialogService? dialogService = null, bool haXMode = false, Func<int>? getCurrentBox = null, IWindowService? windowService = null, BoxSelectionService? selection = null, BulkSlotActionsController? bulkActions = null, IAutoLegalityService? autoLegalityService = null, HardcoreMode? hardcore = null)
    {
        _sav = sav;
        _spriteRenderer = spriteRenderer;
        _slotService = slotService;
        var sessionId = slotService?.SessionId ?? Guid.Empty;
        _sessionId = sessionId == Guid.Empty ? Guid.NewGuid() : sessionId;
        _windowService = windowService;
        _dialogService = dialogService;
        _autoLegalityService = autoLegalityService;
        _getCurrentBox = getCurrentBox ?? (() => 0);
        _haXMode = haXMode;
        _hardcore = hardcore;
        _selection = selection;
        _bulkActions = bulkActions;

        if (_selection is not null)
            _selection.SelectionChanged += OnSelectionChanged;

        LoadParty();
    }

    private bool IsSessionCurrent() => _slotService is null
        || (_sessionId != Guid.Empty && _slotService.IsCurrentSession(_sessionId));

    partial void OnSelectedIndexChanged(int value)
    {
        for (int i = 0; i < Slots.Count; i++)
            Slots[i].IsSelected = i == value;
        OnPropertyChanged(nameof(SelectedSlot));
    }

    private void LoadParty()
    {
        var previousIndex = SelectedIndex;
        Slots.Clear();
        
        var partyCount = _sav.PartyCount;
        
        for (int i = 0; i < 6; i++)
        {
            // LGPE stores party in a PokeList structure inside boxes.
            // On blank saves GetPartySlotAtIndex throws if the list is empty.
            PKM pk;
            try { pk = _sav.GetPartySlotAtIndex(i); }
            catch { pk = _sav.BlankPKM; }
            var isEmpty = pk.Species == 0 || i >= partyCount;
            var state = isEmpty ? default : PKHeX.Application.Models.SpriteSlotState.Create(pk, _sav, StorageSlotType.Party, (StorageSlotSource)((int)StorageSlotSource.Party1 << i), _haXMode);
            
            Slots.Add(new PartySlotData
            {
                Slot = i,
                Species = pk.Species,
                Sprite = isEmpty ? _spriteRenderer.GetEmptySlot() : _spriteRenderer.GetSlotSprite(pk, state),
                SemanticSummary = isEmpty ? string.Empty : SpriteStateDescription.Describe(pk, state),
                IsEmpty = isEmpty,
                IsShiny = !isEmpty && pk.IsShiny,
                Nickname = isEmpty ? string.Empty : pk.Nickname,
                SpeciesName = isEmpty ? string.Empty : StringResourceLookup.Species(pk.Species),
                Level = pk.CurrentLevel,
                Gender = (byte)pk.Gender,
                HeldItem = (ushort)pk.HeldItem,
                HeldItemName = pk.HeldItem > 0 ? StringResourceLookup.Item(pk.HeldItem) : string.Empty,
                IsEgg = pk.IsEgg,
                CurrentHp = (ushort)pk.Stat_HPCurrent,
                MaxHp = (ushort)pk.Stat_HPMax,
                ShowdownSummary = isEmpty ? string.Empty : new ShowdownSet(pk).Text,
                IsLegal = !state.Illegal,
                IsSelected = false,
                IsMarked = _selection?.IsMarked(SlotLocation.FromParty(i)) ?? false,
            });
        }
        
        // Restore selection position (clamped to valid range)
        SelectedIndex = Math.Clamp(previousIndex, 0, Slots.Count - 1);
        OnSelectedIndexChanged(SelectedIndex);
    }

    [RelayCommand]
    private void SelectSlotByClick(PartySlotData? slot)
    {
        if (slot is null)
            return;

        SelectedIndex = slot.Slot;
        // Organizer selection mode: a plain click marks/unmarks instead of only moving the cursor.
        if (IsSelectionModeEnabled)
            ToggleMark(slot);
    }

    [RelayCommand]
    private void ActivateSlot()
    {
        if (!IsSessionCurrent() || SelectedIndex < 0 || SelectedIndex >= Slots.Count)
            return;

        var slot = Slots[SelectedIndex];
        if (IsSelectionModeEnabled)
        {
            // Keyboard counterpart of a selection-mode click (Enter/Space toggle the cursor slot).
            ToggleMark(slot);
            return;
        }
        SlotActivated?.Invoke(slot.Slot);
    }

    [RelayCommand]
    private void MoveSelection(string direction)
    {
        if (Slots.Count == 0) return;

        int newIndex = direction switch
        {
            "Up" => SelectedIndex > 0 ? SelectedIndex - 1 : SelectedIndex,
            "Down" => SelectedIndex < Slots.Count - 1 ? SelectedIndex + 1 : SelectedIndex,
            _ => SelectedIndex
        };

        SelectedIndex = newIndex;
    }

    public void RefreshParty() => LoadParty();

    /// <summary>Opens (or focuses) this Party viewer as a modeless workspace.</summary>
    [RelayCommand]
    private void OpenDetachedTool()
    {
        if (IsSessionCurrent())
            _windowService?.ShowTool(this, LocalizedStrings.Instance["Tab_Party"]);
    }
    
    [RelayCommand]
    private void ViewSlot(PartySlotData? slot)
    {
        if (slot is null || slot.IsEmpty || !IsSessionCurrent())
            return;
        
        if (_slotService is not null)
            _slotService.RequestView(_sessionId, SlotLocation.FromParty(slot.Slot));
        else
            ViewSlotRequested?.Invoke(slot.Slot);
    }
    
    [RelayCommand]
    private void SetSlot(PartySlotData? slot)
    {
        if (slot is null || !IsSessionCurrent() || !IsEditingAllowed)
            return;
        
        if (_slotService is not null)
            _slotService.RequestSet(_sessionId, SlotLocation.FromParty(slot.Slot));
        else
            SetSlotRequested?.Invoke(slot.Slot);
    }
    
    [RelayCommand]
    private void DeleteSlot(PartySlotData? slot)
    {
        if (slot is null || slot.IsEmpty || !IsSessionCurrent())
            return;
        
        if (_slotService is not null)
            _slotService.RequestDelete(_sessionId, SlotLocation.FromParty(slot.Slot));
        else
            DeleteSlotRequested?.Invoke(slot.Slot);
    }

    /// <summary>
    /// Runs the Auto Legality Mod engine's in-place legalization on this one party slot directly,
    /// without opening the Legality report dialog first. Mirrors upstream PKHeX WinForms ALM's
    /// right-click "Legalize" context menu entry.
    /// </summary>
    [RelayCommand]
    private async Task LegalizeSlot(PartySlotData? slot)
    {
        if (slot is null || slot.IsEmpty || _autoLegalityService is null || _isLegalizingSlot || !IsSessionCurrent() || !IsEditingAllowed)
            return;

        _isLegalizingSlot = true;
        try
        {
            var sav = _sav;
            var service = _autoLegalityService;
            var pk = GetSlotPKM(slot.Slot);
            var result = await Task.Run(() => service.TryLegalizeExisting(sav, pk));

            if (!IsSessionCurrent())
                return;

            if (result.Success && result.Pokemon is not null)
            {
                SetSlotPKM(slot.Slot, result.Pokemon);
            }
            else if (_dialogService is not null)
            {
                await _dialogService.ShowErrorAsync(
                    LocalizedStrings.Instance["Slot_LegalizeFailedTitle"],
                    result.Messages.Count > 0 ? result.MessageText : LocalizedStrings.Instance["Slot_LegalizeFailedMessage"]);
            }
        }
        finally
        {
            _isLegalizingSlot = false;
        }
    }

    /// <summary>
    /// Moves a Party Pokémon into the first empty slot in the currently displayed Box. This is
    /// the inverse of the Box viewer's cross-tab move action.
    /// </summary>
    [RelayCommand]
    private async Task MoveToBox(PartySlotData? slot)
    {
        if (slot is null || slot.IsEmpty || _slotService is null || !_sav.HasBox || _sav.BoxCount <= 0 || !IsSessionCurrent())
            return;

        var currentBox = Math.Clamp(_getCurrentBox(), 0, _sav.BoxCount - 1);
        var destination = -1;
        for (var i = 0; i < _sav.BoxSlotCount; i++)
        {
            if (_sav.GetBoxSlotAtIndex(currentBox, i).Species == 0)
            {
                destination = i;
                break;
            }
        }

        if (destination < 0)
        {
            if (_dialogService is not null)
                await _dialogService.ShowErrorAsync(
                    LocalizedStrings.Instance["Slot_NoEmptyBoxTitle"],
                    LocalizedStrings.Instance["Slot_NoEmptyBoxMessage"]);
            return;
        }

        _slotService.RequestMove(_sessionId, SlotLocation.FromParty(slot.Slot), SlotLocation.FromBox(currentBox, destination), clone: false);
    }

    /// <summary>
    /// Set by the host when a Bank is available, which shows the "Send to Bank" slot action. The
    /// deposit (including the rule that the last party member cannot leave) is performed by the host
    /// in response to <see cref="SendToBankRequested"/>.
    /// </summary>
    public bool CanSendToBank { get; set; }

    /// <summary>Raised to deposit a party Pokémon into the Bank (and remove it from the party).</summary>
    public event Action<SlotLocation>? SendToBankRequested;

    [RelayCommand]
    private void SendToBank(PartySlotData? slot)
    {
        if (slot is null || slot.IsEmpty || !CanSendToBank || !IsSessionCurrent())
            return;

        SendToBankRequested?.Invoke(SlotLocation.FromParty(slot.Slot));
    }

    /// <summary>
    /// Gets the PKM at the specified slot.
    /// </summary>
    public PKM GetSlotPKM(int slot) => _sav.GetPartySlotAtIndex(slot);

    /// <summary>Creates a session-bound payload for a slot in the current party.</summary>
    public SlotDragData CreateDragData(int slot) => new(SlotLocation.FromParty(slot), SessionId);

    /// <summary>
    /// Sets the PKM at the specified party slot and refreshes the display.
    /// </summary>
    public void SetSlotPKM(int slot, PKM pk)
    {
        if (!IsSessionCurrent())
            return;

        _sav.SetPartySlotAtIndex(pk, slot);
        _sav.State.Edited = true;
        RefreshParty();
    }

    [RelayCommand]
    private void RequestMove((SlotDragData data, PartySlotData dest, bool clone) param)
    {
        if (_slotService is null || !IsSessionCurrent() || param.data.SessionId != SessionId)
            return;
        if (param.clone && !IsEditingAllowed)
        {
            _ = AllowsOrExplainAsync(SaveAction.Duplicate);
            return;
        }

        _slotService.RequestMove(param.data.SessionId, param.data.Source, param.dest.Location, param.clone);
    }

    /// <summary>Raised when a dropped OS file turns out to be a save file, so the host can open it.</summary>
    public event Action<string>? SaveFileDropRequested;

    /// <summary>
    /// Handles one or more OS files dropped onto a party slot. A single file replaces the target
    /// slot (subject to format compatibility); multiple files are placed into the party's next
    /// empty slots in order. Reuses the same detection/conversion pipeline as folder import.
    /// </summary>
    public async Task HandleFileDropAsync(IReadOnlyList<string> paths, int targetSlot)
    {
        if (paths.Count == 0 || !IsSessionCurrent())
            return;

        if (paths.Count == 1)
        {
            var result = new ImportEntityFileUseCase().Execute(_sav, paths[0]);
            switch (result.Kind)
            {
                case EntityFileDropKind.SaveFile:
                    if (IsSessionCurrent())
                        SaveFileDropRequested?.Invoke(paths[0]);
                    return;
                case EntityFileDropKind.Entity:
                    // A dropped save file still opens; a dropped Pokémon would be a new one.
                    if (!await AllowsOrExplainAsync(SaveAction.CreatePokemon))
                        return;
                    if (_slotService is not null)
                        await _slotService.RequestReplaceAsync(_sessionId, SlotLocation.FromParty(targetSlot), result.Entity!, result.Source);
                    else
                        SetSlotPKM(targetSlot, result.Entity!);
                    return;
                default:
                    if (_dialogService is not null)
                        await _dialogService.ShowErrorAsync(LocalizedStrings.Instance["PartyViewer_ImportFailedTitle"], result.Message ?? LocalizedStrings.Instance["PartyViewer_FileCouldNotBeImported"]);
                    return;
            }
        }

        if (!await AllowsOrExplainAsync(SaveAction.CreatePokemon))
            return;
        var candidates = new List<PKM>();
        foreach (var path in paths)
        {
            var result = new ImportEntityFileUseCase().Execute(_sav, path);
            if (result.Kind == EntityFileDropKind.Entity)
                candidates.Add(result.Entity!);
        }

        var placed = 0;
        var slot = 0;
        foreach (var pk in candidates)
        {
            if (!IsSessionCurrent())
                break;

            while (slot < Slots.Count && !Slots[slot].IsEmpty)
                slot++;
            if (slot >= Slots.Count)
                break;

            SetSlotPKM(slot, pk);
            placed++;
            slot++;
        }

        if (_dialogService is not null)
        {
            var message = LocalizedStrings.Instance.Format("PartyViewer_PlacedPokemonIntoParty", placed, paths.Count);
            var skipped = paths.Count - placed;
            if (skipped > 0)
                message += "\n" + LocalizedStrings.Instance.Format("PartyViewer_FilesSkipped", skipped);
            await _dialogService.ShowInformationAsync(LocalizedStrings.Instance["PartyViewer_ImportFilesTitle"], message);
        }
    }
}
