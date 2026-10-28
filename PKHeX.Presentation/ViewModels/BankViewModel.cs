using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;

namespace PKHeX.Presentation.ViewModels;

/// <summary>One Bank index backup offered for restore.</summary>
public sealed record BankBackupRow(SaveBackupEntry Entry, string Label);

/// <summary>
/// The Bank: a persistent, save-independent vault shown in its own top-level window
/// (<see cref="IWindowService.ShowPersistent"/>, which survives the per-save tool-window teardown).
/// One instance lives for the whole app session, so it never holds a <see cref="SaveFile"/>:
/// withdrawals are delegated to <see cref="Target"/> (the main window, which owns the open save)
/// and follow the hold-until-saved contract of <see cref="BankHoldTracker"/>.
/// </summary>
public partial class BankViewModel : ViewModelBase
{
    private const int Columns = 6;

    private readonly IBankService _bank;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly IDialogService? _dialogService;
    private readonly ISaveBackupService? _backupService;
    private readonly IWindowService? _windowService;
    private readonly IBoxWallpaperCatalog? _wallpaperCatalog;
    private readonly BankHoldTracker? _tracker;
    private readonly HardcoreMode? _hardcore;
    private IBankSaveTarget? _target;

    // Marks are entry ids, not slot positions, so they survive paging between Bank boxes.
    private readonly HashSet<Guid> _marked = [];

    /// <summary>Multi-select mode: a slot click toggles its mark instead of only moving the cursor.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkAllInBoxCommand))]
    private bool _isSelectionModeEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoxPositionText))]
    private int _currentBox;

    [ObservableProperty]
    private string _boxName = string.Empty;

    /// <summary>Two-way bound rename field; committed by <see cref="RenameBoxCommand"/>.</summary>
    [ObservableProperty]
    private string _editableBoxName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWallpaperColor))]
    private string? _wallpaperColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWallpaperImage))]
    private byte[]? _wallpaperImage;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private ObservableCollection<BankSlotData> _slots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreSelectedBackupCommand))]
    private BankBackupRow? _selectedBackup;

    public BankViewModel(
        IBankService bank,
        ISpriteRenderer spriteRenderer,
        IDialogService? dialogService = null,
        ISaveBackupService? backupService = null,
        IWindowService? windowService = null,
        IBoxWallpaperCatalog? wallpaperCatalog = null,
        BankHoldTracker? tracker = null,
        HardcoreMode? hardcore = null)
    {
        _bank = bank;
        _spriteRenderer = spriteRenderer;
        _dialogService = dialogService;
        _backupService = backupService;
        _windowService = windowService;
        _wallpaperCatalog = wallpaperCatalog;
        _tracker = tracker;
        if (_tracker is not null)
            _tracker.HoldsChanged += RefreshCurrentBox;
        _hardcore = hardcore;
        if (_hardcore is not null)
            _hardcore.Changed += ImportArchiveCommand.NotifyCanExecuteChanged;

        LoadBox(0);
        RefreshBackups();
    }

    /// <summary>The save withdrawals go into. Set by the host; changes as saves open and close.</summary>
    public IBankSaveTarget? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value))
                return;
            if (_target is not null)
                _target.TargetChanged -= OnTargetChanged;
            _target = value;
            if (_target is not null)
                _target.TargetChanged += OnTargetChanged;
            OnTargetChanged();
        }
    }

    public int BoxCount => _bank.BoxCount;
    public int SlotsPerBox => _bank.SlotsPerBox;
    public bool IsReadOnly => _bank.IsReadOnly;
    public bool IsWritable => !IsReadOnly;

    /// <summary>
    /// Importing files brings Pokémon into the Bank that were never in a save, so Hardcore mode
    /// closes it; deposits and withdrawals are moves and stay open.
    /// </summary>
    public bool CanImportArchive => IsWritable && (_hardcore?.Allows(SaveAction.CreatePokemon) ?? true);
    public bool CanPickWallpaper => _wallpaperCatalog is not null && _windowService is not null;
    public bool HasWallpaperColor => WallpaperColor is not null;
    public bool HasWallpaperImage => WallpaperImage is not null;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public string BoxPositionText => LocalizedStrings.Instance.Format("Bank_BoxPosition", CurrentBox + 1, BoxCount);
    public string OccupancyText => $"{Slots.Count(s => !s.IsEmpty)} / {Slots.Count}";
    public string TotalText => LocalizedStrings.Instance.Format("Bank_TotalStored", _bank.GetAll().Count);

    /// <summary>Joined load/recovery notes from the Bank service (e.g. recovered from backup, missing files).</summary>
    public string WarningText => string.Join(Environment.NewLine, _bank.Warnings);
    public bool HasWarnings => _bank.Warnings.Count > 0;

    public string TargetText => _target?.WithdrawTargetDescription is { Length: > 0 } description
        ? LocalizedStrings.Instance.Format("Bank_WithdrawTarget", description)
        : LocalizedStrings.Instance["Bank_NoSaveOpen"];

    public ObservableCollection<BankBackupRow> IndexBackups { get; } = [];
    public bool HasIndexBackups => IndexBackups.Count > 0;

    public BankSlotData? SelectedSlot => SelectedIndex >= 0 && SelectedIndex < Slots.Count ? Slots[SelectedIndex] : null;

    partial void OnSelectedIndexChanged(int value)
    {
        for (var i = 0; i < Slots.Count; i++)
            Slots[i].IsSelected = i == value;
        OnPropertyChanged(nameof(SelectedSlot));
        WithdrawSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnTargetChanged()
    {
        OnPropertyChanged(nameof(TargetText));
        WithdrawSelectedCommand.NotifyCanExecuteChanged();
        WithdrawMarkedCommand.NotifyCanExecuteChanged();
    }

    private void LoadBox(int box)
    {
        var boxCount = BoxCount;
        box = Math.Clamp(box, 0, Math.Max(0, boxCount - 1));

        var previousIndex = SelectedIndex;
        CurrentBox = box;
        BoxName = _bank.GetBoxName(box);
        EditableBoxName = BoxName;
        ApplyWallpaper(_bank.GetBoxWallpaper(box));

        var missing = _bank.GetEntriesWithMissingData().ToHashSet();
        var bySlot = new Dictionary<int, BankEntry>();
        foreach (var entry in _bank.GetBox(box))
            bySlot.TryAdd(entry.Slot, entry);

        var newSlots = new ObservableCollection<BankSlotData>();
        for (var slot = 0; slot < SlotsPerBox; slot++)
        {
            if (bySlot.TryGetValue(slot, out var entry))
            {
                var info = entry.Info;
                var isMissing = missing.Contains(entry.Id);
                newSlots.Add(new BankSlotData
                {
                    Slot = slot,
                    Id = entry.Id,
                    Species = info.Species,
                    Form = info.Form,
                    IsShiny = info.IsShiny,
                    Nickname = info.Nickname,
                    SpeciesName = StringResourceLookup.Species(info.Species),
                    Level = info.Level,
                    HeldItem = info.HeldItem,
                    HeldItemName = info.HeldItem > 0 ? StringResourceLookup.Item(info.HeldItem) : string.Empty,
                    IsEgg = info.IsEgg,
                    Format = info.Format,
                    SourceLabel = info.SourceLabel,
                    AddedUtc = entry.AddedUtc,
                    IsPending = entry.Pending is not null,
                    IsPendingDeposit = entry.IsPendingDeposit,
                    PendingSaveLabel = entry.Pending?.SaveLabel ?? string.Empty,
                    IsDataMissing = isMissing,
                    Sprite = isMissing ? null : TryRenderSprite(entry),
                });
            }
            else
            {
                newSlots.Add(new BankSlotData { Slot = slot });
            }
        }

        Slots = newSlots;
        if (_marked.Count > 0)
            _marked.IntersectWith(_bank.GetAll().Select(e => e.Id));
        OnMarksChanged();
        SelectedIndex = Math.Clamp(previousIndex, 0, Math.Max(0, Slots.Count - 1));
        OnSelectedIndexChanged(SelectedIndex);
        OnPropertyChanged(nameof(OccupancyText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(BoxCount));
        OnPropertyChanged(nameof(BoxPositionText));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(IsWritable));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(HasWarnings));
        ImportArchiveCommand.NotifyCanExecuteChanged();
        AddBoxCommand.NotifyCanExecuteChanged();
    }

    private void ApplyWallpaper(BankWallpaper wallpaper)
    {
        WallpaperColor = wallpaper.Kind == BankWallpaperKind.Color ? BankWallpaperPalette.TryGetColor(wallpaper.Value) : null;
        WallpaperImage = wallpaper.Kind == BankWallpaperKind.Art ? _wallpaperCatalog?.GetImageBytes(wallpaper.Value) : null;
    }

    private byte[]? TryRenderSprite(BankEntry entry)
    {
        try
        {
            var raw = _bank.GetRawData(entry.Id);
            if (raw is null)
                return null;
            var pk = BankEntryInfoFactory.TryReconstruct(raw, entry.Info);
            return pk is null ? null : _spriteRenderer.GetSprite(pk);
        }
        catch
        {
            return null; // a sprite is decoration; never let it break the grid
        }
    }

    /// <summary>Re-reads the current box (e.g. after a deposit from a save, a hold change, or another app window's write).</summary>
    public void RefreshCurrentBox() => LoadBox(CurrentBox);

    [RelayCommand]
    private void Refresh()
    {
        LoadBox(CurrentBox);
        RefreshBackups();
    }

    [RelayCommand]
    private void PreviousBox()
    {
        var newBox = CurrentBox - 1;
        if (newBox < 0)
            newBox = BoxCount - 1;
        LoadBox(newBox);
    }

    [RelayCommand]
    private void NextBox()
    {
        var newBox = CurrentBox + 1;
        if (newBox >= BoxCount)
            newBox = 0;
        LoadBox(newBox);
    }

    [RelayCommand(CanExecute = nameof(IsWritable))]
    private void AddBox()
    {
        var index = _bank.AddBox();
        LoadBox(index);
    }

    [RelayCommand]
    private void SelectSlotByClick(BankSlotData? slot)
    {
        if (slot is null)
            return;
        SelectedIndex = slot.Slot;
        if (IsSelectionModeEnabled)
            ToggleMark(slot);
    }

    public int MarkedCount => _marked.Count;
    public bool HasMarks => _marked.Count > 0;
    public string MarkedCountText => LocalizedStrings.Instance.Format("Organizer_MarkedCount", MarkedCount);

    /// <summary>Marked entry ids in Bank order (box, then slot), for a stable withdrawal order.</summary>
    public IReadOnlyList<Guid> MarkedIds => _bank.GetAll().Where(e => _marked.Contains(e.Id)).Select(e => e.Id).ToList();

    partial void OnIsSelectionModeEnabledChanged(bool value)
    {
        if (!value)
            ClearMarks();
    }

    private void ToggleMark(BankSlotData slot)
    {
        if (slot.Id is not { } id)
            return;
        if (!_marked.Remove(id))
            _marked.Add(id);
        OnMarksChanged();
    }

    private void OnMarksChanged()
    {
        foreach (var slot in Slots)
            slot.IsMarked = slot.Id is { } id && _marked.Contains(id);
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(MarkedCountText));
        ClearMarksCommand.NotifyCanExecuteChanged();
        WithdrawMarkedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsSelectionModeEnabled))]
    private void MarkAllInBox()
    {
        foreach (var slot in Slots)
        {
            if (slot.Id is { } id)
                _marked.Add(id);
        }
        OnMarksChanged();
    }

    [RelayCommand(CanExecute = nameof(HasMarks))]
    private void ClearMarks()
    {
        _marked.Clear();
        OnMarksChanged();
    }

    private bool CanWithdrawMarked => !IsReadOnly && HasMarks && _target is { CanReceiveWithdrawal: true };

    /// <summary>
    /// Withdraws every marked entry into the open save in one confirmed, undoable action. With
    /// "Send marked to Bank" in another app window, this is how marked Pokémon move between two open saves.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWithdrawMarked))]
    private async Task WithdrawMarkedAsync()
    {
        if (_target is null)
            return;

        var withdrawn = await _target.WithdrawManyAsync(MarkedIds);
        foreach (var id in withdrawn)
            _marked.Remove(id);
        RefreshCurrentBox();
    }

    [RelayCommand]
    private void MoveSelection(string direction)
    {
        if (Slots.Count == 0)
            return;

        SelectedIndex = direction switch
        {
            "Left" => SelectedIndex > 0 ? SelectedIndex - 1 : SelectedIndex,
            "Right" => SelectedIndex < Slots.Count - 1 ? SelectedIndex + 1 : SelectedIndex,
            "Up" => SelectedIndex >= Columns ? SelectedIndex - Columns : SelectedIndex,
            "Down" => SelectedIndex + Columns < Slots.Count ? SelectedIndex + Columns : SelectedIndex,
            _ => SelectedIndex,
        };
    }

    [RelayCommand]
    private void RenameBox()
    {
        var name = (EditableBoxName ?? string.Empty).Trim();
        if (name.Length > 32)
            name = name[..32];
        _bank.SetBoxName(CurrentBox, name);
        BoxName = _bank.GetBoxName(CurrentBox);
        EditableBoxName = BoxName;
    }

    [RelayCommand]
    private void SetWallpaper(BankWallpaper wallpaper)
    {
        _bank.SetBoxWallpaper(CurrentBox, wallpaper);
        ApplyWallpaper(_bank.GetBoxWallpaper(CurrentBox));
    }

    /// <summary>Opens the wallpaper picker (Phase 2), reusing the bundled native box-art assets.</summary>
    [RelayCommand]
    private async Task PickWallpaperAsync()
    {
        if (_wallpaperCatalog is null || _windowService is null)
            return;

        var box = CurrentBox;
        var picker = new WallpaperPickerViewModel(_wallpaperCatalog, _bank.GetBoxWallpaper(box));
        await _windowService.ShowDialogAsync(picker, LocalizedStrings.Instance["Bank_PickWallpaperTitle"]);

        if (picker.Result is { } chosen)
        {
            _bank.SetBoxWallpaper(box, chosen);
            if (box == CurrentBox)
                ApplyWallpaper(_bank.GetBoxWallpaper(box));
        }
    }

    private bool CanWithdraw => !IsReadOnly
        && SelectedSlot is { CanWithdraw: true }
        && _target is { CanReceiveWithdrawal: true };

    /// <summary>
    /// Withdraws the selected entry into the open save's currently viewed box. The entry stays in the
    /// Bank, shown as pending, until that save is written to disk (see <see cref="BankHoldTracker"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWithdraw))]
    private async Task WithdrawSelectedAsync()
    {
        if (SelectedSlot?.Id is not { } id || _target is null)
            return;

        await _target.WithdrawAsync(id);
        RefreshCurrentBox();
    }

    /// <summary>Selects a slot and withdraws it (context menu / double-click).</summary>
    [RelayCommand]
    private async Task WithdrawSlotAsync(BankSlotData? slot)
    {
        if (slot is null)
            return;
        SelectedIndex = slot.Slot;
        if (CanWithdraw)
            await WithdrawSelectedAsync();
    }

    /// <summary>Exports every Bank entry to a user-chosen folder as loose entity files + manifest.json.</summary>
    [RelayCommand]
    private async Task ExportArchiveAsync()
    {
        if (_dialogService is null)
            return;

        var folder = await _dialogService.OpenFolderAsync(LocalizedStrings.Instance["Bank_SelectExportFolderTitle"]);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        BankArchiveExportResult result;
        try
        {
            result = new ExportBankArchiveUseCase().Execute(_bank, folder);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(LocalizedStrings.Instance["Bank_ExportArchiveTitle"], BankMessages.Describe(BankErrorKind.Io, ex.Message));
            return;
        }

        await _dialogService.ShowInformationAsync(
            LocalizedStrings.Instance["Bank_ExportArchiveTitle"],
            LocalizedStrings.Instance.Format("Bank_ExportArchiveResult", result.Exported, result.Skipped));
    }

    /// <summary>Imports every recognized entity file from a user-chosen folder — idempotent by content hash, so re-importing an overlapping archive never duplicates entries.</summary>
    [RelayCommand(CanExecute = nameof(CanImportArchive))]
    private async Task ImportArchiveAsync()
    {
        if (_dialogService is null || !CanImportArchive)
            return;

        var folder = await _dialogService.OpenFolderAsync(LocalizedStrings.Instance["Bank_SelectImportFolderTitle"]);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        BankArchiveImportResult result;
        try
        {
            result = new ImportBankArchiveUseCase().Execute(_bank, folder, LocalizedStrings.Instance["Bank_ArchiveImportSource"]);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(LocalizedStrings.Instance["Bank_ImportArchiveTitle"], BankMessages.Describe(BankErrorKind.Io, ex.Message));
            return;
        }

        RefreshCurrentBox();
        await _dialogService.ShowInformationAsync(
            LocalizedStrings.Instance["Bank_ImportArchiveTitle"],
            LocalizedStrings.Instance.Format("Bank_ImportArchiveResult", result.Imported, result.Duplicates, result.Skipped));
    }

    /// <summary>
    /// Lists the Bank index's own backups (the Backup Manager mechanism from issue #135, under the
    /// Bank's fixed <see cref="BankConstants.BackupIdentity"/>), newest first.
    /// </summary>
    [RelayCommand]
    private void RefreshBackups()
    {
        IndexBackups.Clear();
        if (_backupService is not null)
        {
            IReadOnlyList<SaveBackupEntry> entries;
            try
            {
                entries = _backupService.ListBackups(BankConstants.BackupIdentity).Entries;
            }
            catch
            {
                entries = [];
            }

            foreach (var entry in entries.OrderByDescending(e => e.TimestampUtc))
            {
                var label = LocalizedStrings.Instance.Format(
                    "Bank_BackupLabel",
                    entry.TimestampUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture),
                    Math.Max(1, (entry.SizeBytes + 1023) / 1024));
                IndexBackups.Add(new BankBackupRow(entry, label));
            }
        }

        SelectedBackup = IndexBackups.FirstOrDefault();
        OnPropertyChanged(nameof(HasIndexBackups));
    }

    private bool CanRestoreBackup => SelectedBackup is not null && _backupService is not null;

    /// <summary>
    /// Replaces the Bank index with the selected backup. The current index is backed up first (so the
    /// restore can itself be undone), and the replacement is written atomically by the Bank service.
    /// Refused while a withdrawal from this Bank is waiting for its save to be written.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private async Task RestoreSelectedBackupAsync()
    {
        var row = SelectedBackup;
        if (row is null || _backupService is null)
            return;

        var loc = LocalizedStrings.Instance;
        if (_tracker is { HeldEntryIds.Count: > 0 })
        {
            if (_dialogService is not null)
                await _dialogService.ShowErrorAsync(loc["Bank_RestoreFailedTitle"], loc["Bank_RestoreBlockedByPending"]);
            return;
        }

        var confirmed = _dialogService is null || await _dialogService.ShowConfirmationAsync(
            loc["Bank_RestoreBackupTitle"],
            loc.Format("Bank_RestoreBackupConfirm", row.Label),
            loc["Bank_RestoreBackupConfirmButton"],
            loc["Common_Cancel"]);
        if (!confirmed)
            return;

        byte[] data;
        try
        {
            data = _backupService.ReadBackup(row.Entry);
        }
        catch (Exception ex)
        {
            if (_dialogService is not null)
                await _dialogService.ShowErrorAsync(loc["Bank_RestoreFailedTitle"], BankMessages.Describe(BankErrorKind.Io, ex.Message));
            return;
        }

        var result = _bank.RestoreIndexFromBackup(data);
        if (!result.Success)
        {
            if (_dialogService is not null)
                await _dialogService.ShowErrorAsync(loc["Bank_RestoreFailedTitle"], BankMessages.Describe(result));
            return;
        }

        _tracker?.Forget();
        LoadBox(Math.Min(CurrentBox, BoxCount - 1));
        RefreshBackups();

        var missing = _bank.GetEntriesWithMissingData().Count;
        StatusText = missing > 0
            ? loc.Format("Bank_RestoredWithMissing", missing)
            : loc["Bank_Restored"];
    }

    /// <summary>Shows a transient note in the Bank window's status line.</summary>
    public void ShowStatus(string text) => StatusText = text;
}
