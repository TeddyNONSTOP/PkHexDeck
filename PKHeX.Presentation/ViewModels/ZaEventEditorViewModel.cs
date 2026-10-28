using System.Collections;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Models.Events;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class ZaEventEditorViewModel : EventEditorViewModel, ICloseableDialog, IDisposable
{
    private readonly ZaEventDataSession _session;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<ulong, string> _names = new();
    public ZaEventEditorViewModel(SAV9ZA save, IDialogService dialogs)
    {
        _session = new ZaEventDataSession(save); _dialogs = dialogs;
        Categories = _session.Categories.Select(c => new ZaEventCategoryViewModel(c, _names)).ToArray();
        foreach (var field in Categories.SelectMany(c => c.Records).SelectMany(r => r.Fields)) field.PropertyChanged += FieldChanged;
        _selectedCategory = Categories[0];
        _selectedRecord = _selectedCategory.Records.FirstOrDefault();
        FilterCategories();
        Filter();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }
    public override bool IsSupported => true;
    public Action? CloseRequested { get; set; }
    public IReadOnlyList<ZaEventCategoryViewModel> Categories { get; }
    [ObservableProperty] private ZaEventCategoryViewModel _selectedCategory;
    [ObservableProperty] private ZaEventRecordViewModel? _selectedRecord;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _categorySearch = string.Empty;
    [ObservableProperty] private bool _advanced;
    [ObservableProperty] private bool _hideEmpty = true;
    [ObservableProperty] private IReadOnlyList<ZaEventCategoryViewModel> _visibleCategories = [];
    [ObservableProperty] private IReadOnlyList<ZaEventRecordViewModel> _visibleRecords = [];
    [ObservableProperty] private string _statusText = string.Empty;
    public bool CanApply => _session.Categories.All(c => c.IsValid) && Categories.SelectMany(c => c.Records).SelectMany(r => r.Fields).All(f => !f.HasErrors);
    public bool CanEditSelected => SelectedRecord is not null && (Advanced || SelectedRecord.HasName) && !SelectedRecord.IsEmpty;
    public string ScopeText => LocalizedStrings.Instance.Format("ZaEvents_Count", VisibleRecords.Count, SelectedCategory.Records.Count);
    public bool HasStatus => StatusText.Length != 0;
    public string ValidationText => CanApply ? string.Empty : LocalizedStrings.Instance["ZaEvents_Invalid"];
    partial void OnSelectedCategoryChanged(ZaEventCategoryViewModel value)
    {
        // A selector can temporarily clear its selection when its filtered items change.
        if (value is null)
        {
            SelectedCategory = VisibleCategories.FirstOrDefault() ?? Categories[0];
            return;
        }
        Filter(); SelectedRecord = VisibleRecords.FirstOrDefault(); OnPropertyChanged(nameof(ScopeText));
    }
    partial void OnSelectedRecordChanged(ZaEventRecordViewModel? value) => OnPropertyChanged(nameof(CanEditSelected));
    partial void OnAdvancedChanged(bool value)
    { foreach (var record in Categories.SelectMany(c => c.Records)) record.SetAdvanced(value); OnPropertyChanged(nameof(CanEditSelected)); }
    partial void OnSearchTextChanged(string value) => Filter();
    partial void OnCategorySearchChanged(string value) => FilterCategories();
    partial void OnHideEmptyChanged(bool value) => Filter();
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    private void Filter()
    {
        VisibleRecords = SelectedCategory.Records.Where(r => (!HideEmpty || !r.IsEmpty)
            && (string.IsNullOrWhiteSpace(SearchText) || r.Matches(SearchText))).ToArray();
        OnPropertyChanged(nameof(ScopeText));
    }
    private void FilterCategories()
    {
        VisibleCategories = Categories.Where(c => string.IsNullOrWhiteSpace(CategorySearch) || c.Name.Contains(CategorySearch, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }
    private void FieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ZaEventFieldViewModel.HasErrors)) return;
        OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(ValidationText)); ApplyCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanApply))] private void Apply()
    {
        if (!CanApply) return;
        var count = _session.Commit(); RefreshFields();
        StatusText = LocalizedStrings.Instance.Format("Events_Applied", count); CloseRequested?.Invoke();
    }
    [RelayCommand] private void Reset()
    {
        _session.Reset(); RefreshFields(); StatusText = LocalizedStrings.Instance["Events_Discarded"];
    }
    [RelayCommand] private void Cancel() { Reset(); CloseRequested?.Invoke(); }
    private void RefreshFields()
    {
        var selected = SelectedRecord;
        foreach (var record in Categories.SelectMany(c => c.Records)) record.RefreshValues();
        Filter(); SelectedRecord = selected is not null && VisibleRecords.Contains(selected) ? selected : VisibleRecords.FirstOrDefault();
    }
    [RelayCommand] private async Task LoadNamesAsync()
    {
        var path = await _dialogs.OpenFileAsync(LocalizedStrings.Instance["ZaEvents_LoadNames"], ["*.txt", "*", "*.*"]);
        if (path is null) return;
        try
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException();
            var lines = await File.ReadAllLinesAsync(path);
            SCBlockMetadata.AddExtraKeyNames64(_names, lines);
            foreach (var record in Categories.SelectMany(c => c.Records)) record.RefreshName();
            Filter(); OnPropertyChanged(nameof(CanEditSelected));
            StatusText = LocalizedStrings.Instance.Format("ZaEvents_NamesLoaded", _names.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["ZaEvents_LoadNames"], LocalizedStrings.Instance["ZaEvents_NamesFailed"]); }
    }
    private void RefreshLanguage()
    {
        foreach (var category in Categories) category.RefreshLanguage();
        foreach (var record in Categories.SelectMany(c => c.Records)) record.RefreshLanguage();
        OnPropertyChanged(nameof(ValidationText)); FilterCategories(); Filter();
    }
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        foreach (var field in Categories.SelectMany(c => c.Records).SelectMany(r => r.Fields)) field.PropertyChanged -= FieldChanged;
    }
}
public sealed class ZaEventCategoryViewModel(ZaEventCategory category, IReadOnlyDictionary<ulong, string> names) : ObservableObject
{
    public string Key => category.Name;
    public string Name => LocalizedStrings.Instance[$"ZaEvents_Category_{Key}"];
    public IReadOnlyList<ZaEventRecordViewModel> Records { get; } = category.Records.Select(r => new ZaEventRecordViewModel(r, names)).ToArray();
    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));
}
public sealed class ZaEventRecordViewModel : ObservableObject
{
    private readonly ZaEventRecord _record;
    private readonly IReadOnlyDictionary<ulong, string> _names;
    private bool _advanced;
    public ZaEventRecordViewModel(ZaEventRecord record, IReadOnlyDictionary<ulong, string> names)
    {
        _record = record; _names = names;
        Fields = record.Fields.Select((f, index) => new ZaEventFieldViewModel(f, index, () => CanEdit)).ToArray();
        foreach (var field in Fields) field.PropertyChanged += (_, e) =>
        { if (e.PropertyName is nameof(ZaEventFieldViewModel.ValueText) or nameof(ZaEventFieldViewModel.IsSet)) OnPropertyChanged(nameof(ValueSummary)); };
    }
    public int Index => _record.Index;
    public string Hash => _record.HashText;
    public bool IsEmpty => _record.IsEmpty;
    public bool HasName => _names.ContainsKey(_record.PrimaryHash);
    public string Name => _names.GetValueOrDefault(_record.PrimaryHash) ?? LocalizedStrings.Instance["ZaEvents_Unknown"];
    public bool CanEdit => !IsEmpty && (_advanced || HasName);
    public IReadOnlyList<ZaEventFieldViewModel> Fields { get; }
    public string ValueSummary => string.Join(" / ", Fields.Select(f => f.DisplayValue));
    public string TypeSummary => string.Join(" + ", Fields.Select(f => f.TypeName));
    public bool Matches(string text) => Index.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.Ordinal)
        || Hash.Contains(text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
        || Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || ValueSummary.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    public void SetAdvanced(bool value) { _advanced = value; OnPropertyChanged(nameof(CanEdit)); foreach (var f in Fields) f.RefreshAccess(); }
    public void RefreshName() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(HasName)); OnPropertyChanged(nameof(CanEdit)); foreach (var f in Fields) f.RefreshAccess(); }
    public void RefreshValues() { foreach (var f in Fields) f.RefreshValue(); OnPropertyChanged(nameof(ValueSummary)); }
    public void RefreshLanguage() { RefreshName(); OnPropertyChanged(nameof(TypeSummary)); OnPropertyChanged(nameof(ValueSummary)); foreach (var f in Fields) f.RefreshLanguage(); }
}
public partial class ZaEventFieldViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly ZaEventField _field;
    private readonly Func<bool> _canEdit;
    private bool _refreshing;
    public ZaEventFieldViewModel(ZaEventField field, int index, Func<bool> canEdit)
    { _field = field; Index = index; _canEdit = canEdit; _valueText = DisplayValue; }
    public int Index { get; }
    public bool IsBoolean => _field.Kind == ZaEventFieldKind.Boolean;
    public bool CanEdit => _canEdit();
    public string TypeName => LocalizedStrings.Instance[$"ZaEvents_Type_{_field.Kind}"];
    public string Label => LocalizedStrings.Instance.Format("ZaEvents_Field", Index + 1, TypeName);
    public string DisplayValue => IsBoolean ? LocalizedStrings.Instance[_field.BooleanValue ? "Events_On" : "Events_Off"]
        : _field.Kind == ZaEventFieldKind.Signed64 ? _field.SignedValue.ToString(CultureInfo.InvariantCulture) : _field.RawValue.ToString(CultureInfo.InvariantCulture);
    public bool IsSet
    {
        get => _field.BooleanValue;
        set { if (!CanEdit || !IsBoolean) return; _field.SetBoolean(value); OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); }
    }
    [ObservableProperty] private string _valueText;
    [ObservableProperty] private string _errorText = string.Empty;
    public bool HasErrors => ErrorText.Length != 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => HasErrors && (string.IsNullOrEmpty(propertyName) || propertyName == nameof(ValueText)) ? new[] { ErrorText } : Array.Empty<string>();
    partial void OnValueTextChanged(string value)
    {
        if (_refreshing || IsBoolean) return;
        if (!CanEdit)
        {
            _refreshing = true;
            try { ValueText = DisplayValue; }
            finally { _refreshing = false; }
            return;
        }
        bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        ulong raw;
        if (hex)
        {
            if (!ulong.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out raw)) { ErrorText = LocalizedStrings.Instance["ZaEvents_Invalid"]; return; }
        }
        else if (_field.Kind == ZaEventFieldKind.Signed64)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed)) { ErrorText = LocalizedStrings.Instance["ZaEvents_Invalid"]; return; }
            raw = unchecked((ulong)signed);
        }
        else if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw)) { ErrorText = LocalizedStrings.Instance["ZaEvents_Invalid"]; return; }
        _field.SetRaw(raw); ErrorText = string.Empty; OnPropertyChanged(nameof(DisplayValue));
    }
    partial void OnErrorTextChanged(string value) { OnPropertyChanged(nameof(HasErrors)); ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(ValueText))); }
    public void RefreshValue()
    {
        _refreshing = true;
        try { ValueText = DisplayValue; ErrorText = string.Empty; OnPropertyChanged(nameof(IsSet)); OnPropertyChanged(nameof(DisplayValue)); }
        finally { _refreshing = false; }
    }
    public void RefreshAccess() => OnPropertyChanged(nameof(CanEdit));
    public void RefreshLanguage() { OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(TypeName)); OnPropertyChanged(nameof(DisplayValue)); if (HasErrors) ErrorText = LocalizedStrings.Instance["ZaEvents_Invalid"]; }
}
