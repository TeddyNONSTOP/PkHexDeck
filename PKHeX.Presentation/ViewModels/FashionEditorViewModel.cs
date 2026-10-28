using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class FashionEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SAV8SWSH? _source;
    private readonly FashionUnlock8? _fashion;
    private readonly IDialogService? _dialogs;
    private readonly UndoRedoService? _undo;
    private readonly Stack<byte[]> _previewHistory = new();
    private bool _loading;
    private static readonly string[] RegionKeys = ["Eyewear", "Headwear", "Outerwear", "Tops", "Bags", "Gloves", "Bottoms", "Legwear", "Footwear"];

    public FashionEditorViewModel(SaveFile save, IDialogService? dialogs = null, UndoRedoService? undo = null)
    {
        _dialogs = dialogs;
        _undo = undo;
        _source = save as SAV8SWSH;
        if (_source is null || _source.Fashion.Data.Length < 0xF00) return;
        var snapshot = (SAV8SWSH)_source.Clone();
        _fashion = snapshot.Fashion;
        IsSupported = true;
        // Public Core routines provide authoritative gender/version-aware availability masks.
        var catalog = (SAV8SWSH)_source.Clone();
        catalog.Fashion.Clear();
        catalog.Fashion.UnlockAll();
        var legal = (SAV8SWSH)_source.Clone();
        legal.Fashion.Clear();
        legal.Fashion.UnlockAllLegal();
        var categories = new List<FashionRegionViewModel>();
        for (int i = 0; i < RegionKeys.Length; i++)
        {
            int region = FashionUnlock8.REGION_EYEWEAR + i;
            var owned = _fashion.GetArrayOwnedFlag(region);
            var seen = _fashion.GetArrayNewFlag(region);
            var allMask = catalog.Fashion.GetArrayOwnedFlag(region);
            var legalMask = legal.Fashion.GetArrayOwnedFlag(region);
            var rows = Enumerable.Range(0, owned.Length).Select(index => new FashionUnlockRow(region, index,
                owned[index], seen[index], allMask[index], legalMask[index])).ToArray();
            foreach (var row in rows) row.PropertyChanged += RowChanged;
            categories.Add(new FashionRegionViewModel(RegionKeys[i], region, rows));
        }
        Regions = categories;
        _selectedRegion = Regions[0];
        Filter();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }
    public bool IsSupported { get; }
    public Action? CloseRequested { get; set; }
    public string GameInfo => _source?.Version.ToString() ?? string.Empty;
    public IReadOnlyList<FashionRegionViewModel> Regions { get; } = [];
    [ObservableProperty] private FashionRegionViewModel? _selectedRegion;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private IReadOnlyList<FashionUnlockRow> _visibleItems = [];
    [ObservableProperty] private string _statusText = string.Empty;
    public IReadOnlyList<ComboItem> Filters => Enumerable.Range(0, 6)
        .Select(i => new ComboItem(LocalizedStrings.Instance[$"FashionReview_Filter{i}"], i)).ToArray();
    public bool CanUndoPreview => _previewHistory.Count != 0;
    public string ScopeText => LocalizedStrings.Instance["FashionReview_ScopeAll"];
    partial void OnSelectedRegionChanged(FashionRegionViewModel? value) => Filter();
    partial void OnSearchTextChanged(string value) => Filter();
    partial void OnFilterIndexChanged(int value) => Filter();
    private void Filter() => VisibleItems = SelectedRegion?.Items.Where(row =>
        (string.IsNullOrWhiteSpace(SearchText) || row.Index.ToString().Contains(SearchText, StringComparison.Ordinal)
            || row.Status.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase))
        && (FilterIndex switch { 1 => row.IsOwned, 2 => row.IsNew, 3 => row.IsLegal, 4 => row.IsKnown && !row.IsLegal,
            5 => !row.IsKnown, _ => true })).ToArray() ?? [];
    private void Snapshot()
    {
        if (_fashion is null) return;
        _previewHistory.Push(_fashion.Data.ToArray());
        UndoPreviewCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndoPreview));
    }
    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || _fashion is null || sender is not FashionUnlockRow row
            || e.PropertyName is not (nameof(FashionUnlockRow.IsOwned) or nameof(FashionUnlockRow.IsNew))) return;
        Snapshot();
        var flags = e.PropertyName == nameof(FashionUnlockRow.IsOwned)
            ? _fashion.GetArrayOwnedFlag(row.Region) : _fashion.GetArrayNewFlag(row.Region);
        flags[row.Index] = e.PropertyName == nameof(FashionUnlockRow.IsOwned) ? row.IsOwned : row.IsNew;
        if (e.PropertyName == nameof(FashionUnlockRow.IsOwned)) _fashion.SetArrayOwnedFlag(row.Region, flags);
        else _fashion.SetArrayNewFlag(row.Region, flags);
        Regions.Single(r => r.RegionIndex == row.Region).RefreshCount();
    }
    private void Reload()
    {
        if (_fashion is null) return;
        _loading = true;
        try
        {
            foreach (var region in Regions)
            {
                var owned = _fashion.GetArrayOwnedFlag(region.RegionIndex);
                var seen = _fashion.GetArrayNewFlag(region.RegionIndex);
                foreach (var row in region.Items) { row.IsOwned = owned[row.Index]; row.IsNew = seen[row.Index]; }
                region.RefreshCount();
            }
        }
        finally { _loading = false; }
        Filter();
    }
    private async Task<bool> Confirm(string actionKey)
    {
        if (!IsSupported || _dialogs is null) return false;
        return await _dialogs.ShowConfirmationAsync(LocalizedStrings.Instance[actionKey], ScopeText,
            LocalizedStrings.Instance["EventFlagsEditor_Apply"], LocalizedStrings.Instance["Common_Cancel"]);
    }
    [RelayCommand] private async Task UnlockAllAsync()
    {
        if (!await Confirm("FashionEditorView_UnlockAll")) return;
        Snapshot(); _fashion!.UnlockAll(); Reload();
    }
    [RelayCommand] private async Task UnlockAllLegalAsync()
    {
        if (!await Confirm("FashionEditorView_UnlockAllLegal")) return;
        Snapshot(); _fashion!.UnlockAllLegal(); Reload();
    }
    [RelayCommand] private async Task ResetAsync()
    {
        if (!await Confirm("FashionEditorView_ResetToDefault")) return;
        Snapshot(); _fashion!.Clear(); _fashion.Reset(); Reload();
    }
    [RelayCommand] private async Task ClearAsync()
    {
        if (!await Confirm("FashionEditorView_ClearAll")) return;
        Snapshot(); _fashion!.Clear(); Reload();
    }
    [RelayCommand(CanExecute = nameof(CanUndoPreview))] private void UndoPreview()
    {
        if (_fashion is null || !_previewHistory.TryPop(out var data)) return;
        data.CopyTo(_fashion.Data);
        Reload();
        UndoPreviewCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndoPreview));
    }
    [RelayCommand] private void Refresh()
    {
        if (_source is null || _fashion is null) return;
        _source.Fashion.Data.CopyTo(_fashion.Data);
        _previewHistory.Clear();
        Reload();
        UndoPreviewCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndoPreview));
        StatusText = LocalizedStrings.Instance["FashionReview_Discarded"];
    }
    [RelayCommand] private void Save()
    {
        if (_source is null || _fashion is null) return;
        if (_undo is not null)
        {
            var source = _source; // History retains the save/block, not this dialog or its rows.
            _undo.ApplyBlockChange(source, () => source.Fashion.Data.ToArray(), data => data.CopyTo(source.Fashion.Data), _fashion.Data);
        }
        else if (!_fashion.Data.SequenceEqual(_source.Fashion.Data))
        {
            _fashion.Data.CopyTo(_source.Fashion.Data);
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
    private void RefreshLanguage()
    {
        foreach (var region in Regions) region.RefreshLanguage();
        foreach (var row in Regions.SelectMany(r => r.Items)) row.RefreshLanguage();
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(ScopeText));
        Filter();
    }
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        foreach (var row in Regions.SelectMany(r => r.Items)) row.PropertyChanged -= RowChanged;
    }
}
public sealed class FashionRegionViewModel(string key, int regionIndex, IReadOnlyList<FashionUnlockRow> items) : ObservableObject
{
    public int RegionIndex { get; } = regionIndex;
    public IReadOnlyList<FashionUnlockRow> Items { get; } = items;
    public string Name => LocalizedStrings.Instance[$"FashionReview_Region_{key}"];
    public int ItemCount => Items.Count(r => r.IsOwned);
    public string Summary => LocalizedStrings.Instance.Format("FashionReview_Count", ItemCount);
    public void RefreshCount() { OnPropertyChanged(nameof(ItemCount)); OnPropertyChanged(nameof(Summary)); }
    public void RefreshLanguage() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Summary)); }
}
public partial class FashionUnlockRow(int region, int index, bool owned, bool isNew, bool known, bool legal) : ObservableObject
{
    public int Region { get; } = region;
    public int Index { get; } = index;
    public bool IsKnown { get; } = known;
    public bool IsLegal { get; } = legal;
    public string Name => LocalizedStrings.Instance.Format("FashionReview_Entry", Index);
    public string Status => LocalizedStrings.Instance[IsLegal ? "FashionReview_Legal" : IsKnown ? "FashionReview_Unavailable" : "FashionReview_Unknown"];
    [ObservableProperty] private bool _isOwned = owned;
    [ObservableProperty] private bool _isNew = isNew;
    public void RefreshLanguage() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Status)); }
}
