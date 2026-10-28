using System.Buffers.Binary;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Fashion9EditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SaveFile _source;
    private readonly SaveFile _snapshot;
    private readonly IDialogService? _dialogs;
    private readonly Stack<byte[][]> _history = new();
    private bool _loading;
    public Fashion9EditorViewModel(SaveFile save, IDialogService? dialogs = null)
    {
        _source = save;
        _snapshot = save;
        _dialogs = dialogs;
        IsSupported = save is SAV9SV or SAV9ZA;
        IsSV = save is SAV9SV;
        if (!IsSupported) return;
        var edited = save.State.Edited;
        try { _snapshot = save.Clone(); }
        finally { save.State.Edited = edited; }
        var source = ((ISCBlockArray)save).Accessor;
        var staged = ((ISCBlockArray)_snapshot).Accessor;
        foreach (var definition in Definitions(IsSV))
        {
            var category = new Fashion9Category(definition.Key, definition.Name, definition.Owned,
                source.GetBlock(definition.Key), staged.GetBlock(definition.Key));
            foreach (var row in category.Items) row.PropertyChanged += RowChanged;
            Categories.Add(category);
        }
        if (IsSV) LoadCatalog();
        _selectedCategory = Categories.FirstOrDefault();
        _selectedItem = SelectedCategory?.Items.FirstOrDefault();
        RefreshFilters();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }
    public bool IsSupported { get; }
    public bool IsSV { get; }
    public bool IsZA => IsSupported && !IsSV;
    public Action? CloseRequested { get; set; }
    public List<Fashion9Category> Categories { get; } = [];
    [ObservableProperty] private Fashion9Category? _selectedCategory;
    [ObservableProperty] private Fashion9Row? _selectedItem;
    [ObservableProperty] private string _categorySearch = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private bool _advanced;
    [ObservableProperty] private bool _allCategories;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    [ObservableProperty] private IReadOnlyList<Fashion9Category> _visibleCategories = [];
    [ObservableProperty] private IReadOnlyList<Fashion9Row> _visibleItems = [];
    public bool SupportsOwnedAction => IsZA && (AllCategories || SelectedCategory?.SupportsOwned == true);
    public bool CanSave => IsSupported && Categories.All(c => c.IsValid && c.Items.All(r => !r.HasErrors));
    public bool CanUndo => _history.Count > 0;
    public string ValidationText => CanSave ? string.Empty : LocalizedStrings.Instance["Fashion9Flow_Invalid"];
    public string ScopeText => LocalizedStrings.Instance[AllCategories ? "Fashion9Flow_AllScope" : "Fashion9Flow_CurrentScope"];
    public IReadOnlyList<ComboItem> Filters => Enumerable.Range(0, IsSV || SelectedCategory?.SupportsOwned != true ? 3 : 8)
        .Select(i => new ComboItem(LocalizedStrings.Instance[$"Fashion9Flow_Filter{i}"], i)).ToArray();
    private static IEnumerable<(uint Key, string Name, bool Owned)> Definitions(bool sv)
    {
        if (sv)
        {
            yield return (SaveBlockAccessor9SV.KFashionUnlockedEyewear, "Eyewear", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedGloves, "Gloves", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedBag, "Bag", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedFootwear, "Footwear", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedHeadwear, "Headwear", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedLegwear, "Legwear", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedClothing, "Clothing", false);
            yield return (SaveBlockAccessor9SV.KFashionUnlockedPhoneCase, "PhoneCase", false);
        }
        else
        {
            yield return (SaveBlockAccessor9ZA.KFashionTops, "Tops", true);
            yield return (SaveBlockAccessor9ZA.KFashionBottoms, "Bottoms", true);
            yield return (SaveBlockAccessor9ZA.KFashionAllInOne, "AllInOne", true);
            yield return (SaveBlockAccessor9ZA.KFashionHeadwear, "Headwear", true);
            yield return (SaveBlockAccessor9ZA.KFashionEyewear, "Eyewear", true);
            yield return (SaveBlockAccessor9ZA.KFashionGloves, "Gloves", true);
            yield return (SaveBlockAccessor9ZA.KFashionLegwear, "Legwear", true);
            yield return (SaveBlockAccessor9ZA.KFashionFootwear, "Footwear", true);
            yield return (SaveBlockAccessor9ZA.KFashionSatchels, "Satchels", true);
            yield return (SaveBlockAccessor9ZA.KFashionEarrings, "Earrings", true);
            yield return (SaveBlockAccessor9ZA.KHairMake00StyleHair, "HairStyle", false);
            yield return (SaveBlockAccessor9ZA.KHairMake01StyleBangs, "Bangs", false);
            yield return (SaveBlockAccessor9ZA.KHairMake02ColorHair, "HairColor", false);
            yield return (SaveBlockAccessor9ZA.KHairMake03ColorHair, "HairColor2", false);
            yield return (SaveBlockAccessor9ZA.KHairMake04ColorHair, "HairColor3", false);
            yield return (SaveBlockAccessor9ZA.KHairMake05StyleEyebrow, "EyebrowStyle", false);
            yield return (SaveBlockAccessor9ZA.KHairMake06ColorEyebrow, "EyebrowColor", false);
            yield return (SaveBlockAccessor9ZA.KHairMake07StyleEyes, "EyeStyle", false);
            yield return (SaveBlockAccessor9ZA.KHairMake08ColorEyes, "EyeColor", false);
            yield return (SaveBlockAccessor9ZA.KHairMake09StyleEyelash, "EyelashStyle", false);
            yield return (SaveBlockAccessor9ZA.KHairMake10ColorEyelash, "EyelashColor", false);
            yield return (SaveBlockAccessor9ZA.KHairMake11Lips, "Lips", false);
            yield return (SaveBlockAccessor9ZA.KHairMake12BeautyMark, "BeautyMark", false);
            yield return (SaveBlockAccessor9ZA.KHairMake13Freckles, "Freckles", false);
            yield return (SaveBlockAccessor9ZA.KHairMake14DarkCircles, "DarkCircles", false);
        }
    }
    private void LoadCatalog()
    {
        var catalog = new SAV9SV { Gender = _source.Gender };
        foreach (var category in Categories)
        {
            var block = catalog.Accessor.GetBlock(category.Key);
            for (int offset = 0; offset + 8 <= block.Data.Length; offset += 8)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(block.Data[offset..], FashionItem9.None);
                BinaryPrimitives.WriteUInt32LittleEndian(block.Data[(offset + 4)..], 0);
            }
        }
        PlayerFashionUnlock9.UnlockBase(catalog.Accessor, catalog.Gender);
        foreach (var category in Categories)
        {
            category.BaseIds = FashionItem9.GetArray(catalog.Accessor.GetBlock(category.Key).Data)
                .Select(i => i.Value).Where(v => v != FashionItem9.None).ToHashSet();
            foreach (var row in category.Items) row.SetCatalog(category.BaseIds);
        }
    }
    partial void OnSelectedCategoryChanged(Fashion9Category? value)
    {
        FilterIndex = 0;
        SelectedItem = value?.Items.FirstOrDefault();
        OnPropertyChanged(nameof(Filters)); OnPropertyChanged(nameof(SupportsOwnedAction));
        FilterItems();
    }
    partial void OnCategorySearchChanged(string value) => RefreshFilters();
    partial void OnSearchTextChanged(string value) => FilterItems();
    partial void OnFilterIndexChanged(int value) => FilterItems();
    partial void OnAllCategoriesChanged(bool value)
    { OnPropertyChanged(nameof(ScopeText)); OnPropertyChanged(nameof(SupportsOwnedAction)); }
    private void RefreshFilters()
    {
        VisibleCategories = Categories.Where(c => string.IsNullOrWhiteSpace(CategorySearch)
            || c.Name.Contains(CategorySearch, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        FilterItems();
    }
    private void FilterItems() => VisibleItems = SelectedCategory?.Items.Where(r =>
        (string.IsNullOrWhiteSpace(SearchText) || r.Identity.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
            || r.Value.ToString(CultureInfo.InvariantCulture).Contains(SearchText, StringComparison.Ordinal))
        && (FilterIndex switch { 1 => r.IsEmpty, 2 => r.IsNew, 3 => r.IsOwned, 4 => r.IsEquipped,
            5 => r.IsNewShop, 6 => r.IsNewGroup, 7 => !r.IsOwned && !r.IsEmpty, _ => true })).ToArray() ?? [];
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is Fashion9Row row && e.PropertyName is nameof(Fashion9Row.Value) or nameof(Fashion9Row.Flags))
        {
            // The record bytes still contain the preceding state at this notification.
            PushHistory();
            row.Write();
        }
        NotifyValidation();
    }
    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(ValidationText));
        SaveCommand.NotifyCanExecuteChanged();
        if (SelectedCategory is not null) SelectedCategory.RefreshCount();
    }
    private void PushHistory()
    {
        _history.Push(Categories.Select(c => c.Staged.Data.ToArray()).ToArray());
        UndoCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanUndo));
    }
    private void ReloadAll()
    {
        var selected = SelectedItem;
        _loading = true;
        try { foreach (var category in Categories) category.Reload(); }
        finally { _loading = false; }
        FilterItems();
        SelectedItem = selected is not null && VisibleItems.Contains(selected) ? selected : VisibleItems.FirstOrDefault();
        NotifyValidation();
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task UnlockBaseAsync()
    {
        if (!IsSV || !CanSave || _dialogs is null) return;
        if (!await _dialogs.ShowConfirmationAsync(LocalizedStrings.Instance["Fashion9Flow_UnlockBase"],
            LocalizedStrings.Instance["Fashion9Flow_BaseScope"], LocalizedStrings.Instance["EventFlagsEditor_Apply"], LocalizedStrings.Instance["Common_Cancel"])) return;
        var additions = Categories.ToDictionary(c => c, c => c.BaseIds.Except(c.Items.Where(r => !r.IsEmpty).Select(r => r.Value)).ToArray());
        if (Categories.Any(c => additions[c].Length > c.Items.Count(r => r.IsEmpty)))
        { StatusText = LocalizedStrings.Instance["Fashion9Flow_Full"]; return; }
        PushHistory();
        _loading = true;
        int count = 0;
        try
        {
            foreach (var category in Categories)
            foreach (var pair in category.Items.Where(r => r.IsEmpty).Zip(additions[category]))
            {
                pair.First.Value = pair.Second;
                pair.First.IsNew = true;
                pair.First.Write(); count++;
            }
        }
        finally { _loading = false; }
        ReloadAll();
        StatusText = LocalizedStrings.Instance.Format("Fashion9Flow_Added", count);
    }
    [RelayCommand]
    private async Task UnlockOwnedAsync()
    {
        if (!SupportsOwnedAction || _dialogs is null || !CanSave) return;
        if (!await _dialogs.ShowConfirmationAsync(LocalizedStrings.Instance["Fashion9Flow_UnlockOwned"], ScopeText,
            LocalizedStrings.Instance["EventFlagsEditor_Apply"], LocalizedStrings.Instance["Common_Cancel"])) return;
        PushHistory(); _loading = true;
        try
        {
            foreach (var category in Categories.Where(c => c.SupportsOwned && (AllCategories || ReferenceEquals(c, SelectedCategory))))
            foreach (var row in category.Items.Where(r => !r.IsEmpty)) { row.IsOwned = true; row.Write(); }
        }
        finally { _loading = false; }
        ReloadAll();
    }
    [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo()
    {
        if (!_history.TryPop(out var blocks)) return;
        for (int i = 0; i < Categories.Count; i++) blocks[i].CopyTo(Categories[i].Staged.Data);
        ReloadAll(); UndoCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanUndo));
    }
    [RelayCommand] private void Reset()
    {
        foreach (var category in Categories) category.Source.Data.CopyTo(category.Staged.Data);
        _history.Clear(); ReloadAll(); UndoCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanUndo));
        StatusText = LocalizedStrings.Instance["Fashion9Flow_Discarded"];
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private void Save()
    {
        if (!CanSave) return;
        foreach (var category in Categories)
        {
            if (category.Source.Data.SequenceEqual(category.Staged.Data)) continue;
            category.Staged.Data.CopyTo(category.Source.Data); _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
    private void RefreshLanguage()
    {
        foreach (var c in Categories) { c.RefreshLanguage(); foreach (var r in c.Items) r.RefreshLanguage(); }
        OnPropertyChanged(nameof(Filters)); OnPropertyChanged(nameof(ScopeText)); OnPropertyChanged(nameof(ValidationText)); RefreshFilters();
    }
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        foreach (var row in Categories.SelectMany(c => c.Items)) row.PropertyChanged -= RowChanged;
    }
}

public sealed class Fashion9Category : ObservableObject
{
    private readonly string _nameKey;
    private readonly Dictionary<uint, int> _originalCounts;
    public Fashion9Category(uint key, string name, bool owned, SCBlock source, SCBlock staged)
    {
        Key = key; _nameKey = name; SupportsOwned = owned; Source = source; Staged = staged;
        Items = Enumerable.Range(0, staged.Data.Length / 8).Select(i => new Fashion9Row(staged, i, owned)).ToArray();
        _originalCounts = Items.Where(r => !r.IsEmpty).GroupBy(r => r.Value).ToDictionary(g => g.Key, g => g.Count());
    }
    public uint Key { get; }
    public bool SupportsOwned { get; }
    public SCBlock Source { get; }
    public SCBlock Staged { get; }
    public IReadOnlyList<Fashion9Row> Items { get; }
    public HashSet<uint> BaseIds { get; set; } = [];
    public string Name => LocalizedStrings.Instance[$"Fashion9Flow_Category_{_nameKey}"];
    public string Summary => LocalizedStrings.Instance.Format("Fashion9Flow_Capacity", Items.Count(r => !r.IsEmpty), Items.Count);
    public bool IsValid => Staged.Data.Length % 8 == 0 && Items.Where(r => !r.IsEmpty).GroupBy(r => r.Value)
        .All(g => g.Count() <= Math.Max(1, _originalCounts.GetValueOrDefault(g.Key)));
    public string IconData => "M 5,2 L 8,4 L 11,2 L 15,6 L 12,9 L 11,8 L 11,15 L 5,15 L 5,8 L 4,9 L 1,6 Z";
    public void Reload() { foreach (var row in Items) row.Reload(); RefreshCount(); }
    public void RefreshCount() => OnPropertyChanged(nameof(Summary));
    public void RefreshLanguage() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Summary)); }
}

public partial class Fashion9Row : ObservableObject, INotifyDataErrorInfo
{
    private readonly SCBlock _block;
    private HashSet<uint> _catalog = [];
    private bool _refreshing;
    public Fashion9Row(SCBlock block, int index, bool owned)
    {
        _block = block; Index = index; SupportsOwned = owned;
        _value = BinaryPrimitives.ReadUInt32LittleEndian(block.Data[(index * 8)..]);
        _flags = BinaryPrimitives.ReadUInt32LittleEndian(block.Data[(index * 8 + 4)..]);
        _rawValueText = Value.ToString(CultureInfo.InvariantCulture);
    }
    public int Index { get; }
    public bool SupportsOwned { get; }
    public bool IsEmpty => Value == FashionItem9.None;
    public string Identity => IsEmpty ? LocalizedStrings.Instance["Fashion9Flow_Empty"]
        : LocalizedStrings.Instance.Format(_catalog.Contains(Value) ? "Fashion9Flow_BaseItem" : "Fashion9Flow_UnknownItem", Value);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private uint _value;
    [ObservableProperty] private uint _flags;
    [ObservableProperty] private string _rawValueText;
    [ObservableProperty] private string _errorText = string.Empty;
    public bool HasErrors => ErrorText.Length != 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => HasErrors && (string.IsNullOrEmpty(propertyName) || propertyName == nameof(RawValueText)) ? new[] { ErrorText } : Array.Empty<string>();
    partial void OnValueChanged(uint value)
    {
        _rawValueText = value.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(RawValueText));
    }
    partial void OnRawValueTextChanged(string value)
    {
        if (_refreshing) return;
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!uint.TryParse(hex ? value[2..] : value, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        { ErrorText = LocalizedStrings.Instance["Fashion9Flow_InvalidId"]; return; }
        Value = parsed; ErrorText = string.Empty;
    }
    partial void OnErrorTextChanged(string value)
    { OnPropertyChanged(nameof(HasErrors)); ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RawValueText))); }
    partial void OnFlagsChanged(uint value)
    {
        OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(IsNewShop)); OnPropertyChanged(nameof(IsNewGroup));
        OnPropertyChanged(nameof(IsEquipped)); OnPropertyChanged(nameof(IsOwned)); OnPropertyChanged(nameof(Status));
    }
    private void SetFlag(uint mask, bool value) => Flags = value ? Flags | mask : Flags & ~mask;
    public bool IsNew { get => (Flags & 1) != 0; set => SetFlag(1, value); }
    public bool IsNewShop { get => (Flags & 2) != 0; set { if (SupportsOwned) SetFlag(2, value); } }
    public bool IsNewGroup { get => (Flags & 4) != 0; set { if (SupportsOwned) SetFlag(4, value); } }
    public bool IsEquipped { get => (Flags & 8) != 0; set { if (SupportsOwned) SetFlag(8, value); } }
    public bool IsOwned { get => (Flags & 16) != 0; set { if (SupportsOwned) SetFlag(16, value); } }
    public string Status => string.Join(", ", new[] {
        SupportsOwned && IsOwned ? LocalizedStrings.Instance["Fashion9Editor_ColumnOwned"] : null,
        SupportsOwned && IsEquipped ? LocalizedStrings.Instance["Fashion9Editor_ColumnEquipped"] : null,
        IsNew ? LocalizedStrings.Instance["Fashion9Editor_ColumnNew"] : null,
        SupportsOwned && IsNewShop ? LocalizedStrings.Instance["Fashion9Editor_ColumnShopNew"] : null,
        SupportsOwned && IsNewGroup ? LocalizedStrings.Instance["Fashion9Editor_ColumnGroupNew"] : null,
    }.Where(s => s is not null));
    public void Write()
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_block.Data[(Index * 8)..], Value);
        BinaryPrimitives.WriteUInt32LittleEndian(_block.Data[(Index * 8 + 4)..], Flags);
    }
    public void Reload()
    {
        _refreshing = true;
        try
        {
            Value = BinaryPrimitives.ReadUInt32LittleEndian(_block.Data[(Index * 8)..]);
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(_block.Data[(Index * 8 + 4)..]);
            RawValueText = Value.ToString(CultureInfo.InvariantCulture); ErrorText = string.Empty;
        }
        finally { _refreshing = false; }
    }
    public void SetCatalog(HashSet<uint> ids) { _catalog = ids; OnPropertyChanged(nameof(Identity)); }
    public void RefreshLanguage() { OnPropertyChanged(nameof(Identity)); OnPropertyChanged(nameof(Status)); if (HasErrors) ErrorText = LocalizedStrings.Instance["Fashion9Flow_InvalidId"]; }
}
