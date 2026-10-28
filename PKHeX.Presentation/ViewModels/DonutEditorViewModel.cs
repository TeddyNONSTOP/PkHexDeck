using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class DonutEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SAV9ZA _sav;
    private readonly DonutDataSession _session;
    private readonly IDialogService? _dialogs;
    private DonutPocket9a _pocket => _session.Pocket;
    private bool _closed;
    public Action? CloseRequested { get; set; }
    [ObservableProperty] private string _error = string.Empty;
    public bool HasError => Error.Length != 0;
    public bool CanSave => IsSupported && !_closed && Donuts.All(row => !row.HasError);
    public bool CanUndo => !_closed && _session.CanUndo;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    public DonutEditorViewModel(SaveFile sav, IDialogService? dialogs = null)
    {
        _sav = (SAV9ZA)sav;
        _session = new DonutDataSession(_sav); _dialogs = dialogs;

        // The donut block only exists once the feature is unlocked in-game; on saves without it
        // the accessor substitutes an empty dummy block, and reading any slot would throw.
        IsSupported = _pocket.Data.Length >= DonutPocket9a.MaxCount * Donut9a.Size;
        if (!IsSupported)
            return;

        LoadDonuts();
        LoadFlavorOptions();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((DonutEditorViewModel)recipient).RefreshLanguage());
    }

    public bool IsSupported { get; }

    [ObservableProperty]
    private ObservableCollection<DonutEntryViewModel> _donuts = [];

    [ObservableProperty]
    private DonutEntryViewModel? _selectedDonut;

    // --- Generator ---
    [ObservableProperty]
    private ObservableCollection<DonutFlavorOptionViewModel> _flavorOptions = [];

    [ObservableProperty]
    private int _generateStart;

    [ObservableProperty]
    private int _generateEnd = DonutPocket9a.MaxCount;

    /// <summary>
    /// Builds the selectable flavor list, matching the upstream generator which keeps only flavors whose
    /// two-digit type index (characters 6-7 of the internal name, e.g. <c>sweet_03</c>) is in the range [3, 21].
    /// </summary>
    private void LoadFlavorOptions()
    {
        FlavorOptions.Clear();
        foreach (var (hash, name) in DonutInfo.Flavors)
        {
            if (!TryGetFlavorTypeIndex(name, out _))
                continue;
            FlavorOptions.Add(new DonutFlavorOptionViewModel(name, hash) { IsSelected = true });
        }
    }

    private static bool TryGetFlavorTypeIndex(string name, out int typeIndex)
    {
        typeIndex = -1;
        if (name.Length < 8 || !int.TryParse(name.AsSpan(6, 2), out var value) || value is < 3 or > 21)
            return false;

        typeIndex = value - 3;
        return true;
    }

    private void LoadDonuts()
    {
        int selected = SelectedDonut?.Index ?? 0;
        Donuts.Clear();
        for (int i = 0; i < DonutPocket9a.MaxCount; i++)
        {
            var donut = _pocket.GetDonut(i);
            Donuts.Add(new DonutEntryViewModel(i, donut, () => !_closed, ValidationChanged));
        }

        if (Donuts.Count > 0)
            SelectedDonut = Donuts[Math.Min(selected, Donuts.Count - 1)];
        ValidationChanged();
    }

    private async Task BulkAsync(string actionKey, Action<DonutPocket9a> edit)
    {
        if (_closed || !IsSupported) return;
        var loc = LocalizedStrings.Instance;
        if (_dialogs is not null && !await _dialogs.ShowConfirmationAsync(loc[actionKey], loc["DonutFlow_BulkConfirm"], loc[actionKey], loc["Common_Cancel"])) return;
        if (_closed) return;
        _session.ApplyBulk(edit); LoadDonuts(); Error = string.Empty;
    }
    [RelayCommand] private Task RandomizeAllAsync() => BulkAsync("DonutEditor_RandomizeAll", pocket => pocket.SetAllRandomLv3());
    [RelayCommand] private Task CloneCurrentAsync()
    {
        int index = SelectedDonut?.Index ?? -1;
        return index < 0 ? Task.CompletedTask : BulkAsync("DonutEditor_CloneCurrent", pocket => pocket.CloneAllFromIndex(index));
    }
    [RelayCommand] private Task ShinyAssortmentAsync() => BulkAsync("DonutEditor_ShinyAssortment", pocket => pocket.SetAllAsShinyTemplate());
    [RelayCommand] private async Task CompressAsync()
    {
        if (_closed || !IsSupported) return;
        var loc = LocalizedStrings.Instance;
        if (_dialogs is not null && !await _dialogs.ShowConfirmationAsync(loc["DonutEditor_Compress"], loc["DonutFlow_BulkConfirm"], loc["DonutEditor_Compress"], loc["Common_Cancel"])) return;
        if (_closed) return;
        _session.Compress(); LoadDonuts(); Error = string.Empty;
    }
    [RelayCommand] private void Refresh() { if (_closed || !IsSupported) return; _session.Reset(); LoadDonuts(); Error = string.Empty; }
    [RelayCommand] private void ResetCurrent() { if (_closed || SelectedDonut is null) return; _session.ResetRecord(SelectedDonut.Index); LoadDonuts(); Error = string.Empty; }
    [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo() { if (_closed) return; _session.Undo(); LoadDonuts(); Error = string.Empty; }
    [RelayCommand] private async Task GenerateAsync()
    {
        if (_closed || !IsSupported) return;
        int start = GenerateStart, end = GenerateEnd;
        var hashes = FlavorOptions.Where(option => option.IsSelected).Select(option => option.Hash).ToArray();
        if (start < 0 || end > DonutPocket9a.MaxCount || start >= end || hashes.Length == 0)
        { Error = LocalizedStrings.Instance["DonutFlow_RangeError"]; return; }
        await BulkAsync("DonutEditor_Generate", pocket => pocket.SetRandomShinyTemplateRange(hashes, start, end));
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private void Save()
    {
        if (!CanSave) return;
        if (!_session.TryCommit()) { Error = LocalizedStrings.Instance["DonutFlow_Conflict"]; return; }
        _closed = true; ValidationChanged(); CloseRequested?.Invoke(); Dispose();
    }
    [RelayCommand] private void Cancel() { if (_closed) return; _closed = true; ValidationChanged(); CloseRequested?.Invoke(); Dispose(); }
    [RelayCommand] private async Task ImportAsync()
    {
        if (_closed || _dialogs is null || SelectedDonut is null) return;
        var path = await _dialogs.OpenFileAsync(LocalizedStrings.Instance["DonutFlow_Import"], ["*.donut", "*"]);
        if (!string.IsNullOrEmpty(path)) await ImportPathAsync(path);
    }
    public async Task ImportPathAsync(string path)
    {
        if (_closed || SelectedDonut is null) return;
        int index = SelectedDonut.Index;
        try
        {
            byte[]? data = null;
            await using (var stream = System.IO.File.OpenRead(path))
            {
                if (stream.Length == Donut9a.Size)
                {
                    data = new byte[Donut9a.Size];
                    await stream.ReadExactlyAsync(data);
                }
            }
            if (_closed) return;
            if (data is null) { Error = LocalizedStrings.Instance["DonutFlow_ImportSize"]; return; }
            if (!_session.ImportRecord(index, data)) { Error = LocalizedStrings.Instance["DonutFlow_ImportSize"]; return; }
            LoadDonuts(); Error = string.Empty;
        }
        catch (System.IO.IOException) { Error = LocalizedStrings.Instance["DonutFlow_FileError"]; }
        catch (UnauthorizedAccessException) { Error = LocalizedStrings.Instance["DonutFlow_FileError"]; }
    }
    [RelayCommand] private async Task ExportAsync()
    {
        if (_closed || _dialogs is null || SelectedDonut is null) return;
        var data = _session.ExportRecord(SelectedDonut.Index);
        var path = await _dialogs.SaveFileAsync(LocalizedStrings.Instance["DonutFlow_Export"], $"Donut_{SelectedDonut.Index + 1:000}.donut", ["*.donut"]);
        if (string.IsNullOrEmpty(path)) return;
        try { await System.IO.File.WriteAllBytesAsync(path, data); Error = string.Empty; }
        catch (System.IO.IOException) { Error = LocalizedStrings.Instance["DonutFlow_FileError"]; }
        catch (UnauthorizedAccessException) { Error = LocalizedStrings.Instance["DonutFlow_FileError"]; }
    }
    private void ValidationChanged()
    {
        OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanUndo)); SaveCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged();
    }
    private void RefreshLanguage() { foreach (var row in Donuts) row.RefreshLanguage(); foreach (var option in FlavorOptions) option.RefreshLanguage(); }
    public void Dispose() { _closed = true; WeakReferenceMessenger.Default.UnregisterAll(this); }

}

public partial class DonutFlavorOptionViewModel : ViewModelBase
{
    public DonutFlavorOptionViewModel(string name, ulong hash)
    {
        Name = name;
        Hash = hash;
    }

    public string Name { get; }
    public ulong Hash { get; }
    public string DisplayName => DonutEntryViewModel.LocalizeFlavor(Hash);
    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayName));

    [ObservableProperty]
    private bool _isSelected;
}
