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

public partial class CategorizedEventEditorViewModel : EventEditorViewModel, ICloseableDialog, IDisposable
{
    private readonly EventDataSession _session;
    private readonly IDialogService _dialogs;
    private readonly IReadOnlyList<EventDataRow> _rows;
    public CategorizedEventEditorViewModel(SaveFile save, IDialogService dialogs)
    {
        _dialogs = dialogs;
        _session = EventDataSession.Create(save) ?? throw new ArgumentException(nameof(save));
        _rows = _session.Fields.Select(f => new EventDataRow(f)).ToArray();
        foreach (var row in _rows) row.PropertyChanged += RowChanged;
        Banks = _rows.Select(r => r.Kind).Distinct().Select(k => new EventBankOption(k)).ToArray();
        _selectedBank = Banks[0];
        _selectedRow = _rows.First(r => r.Kind == SelectedBank.Kind && r.Index == 0);
        BuildCategories();
        Filter();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }
    public override bool IsSupported => true;
    public Action? CloseRequested { get; set; }
    public IReadOnlyList<EventBankOption> Banks { get; }
    public IReadOnlyList<EventDataRow> Rows => _rows;
    [ObservableProperty] private EventBankOption _selectedBank;
    [ObservableProperty] private IReadOnlyList<EventCategoryOption> _categories = [];
    [ObservableProperty] private EventCategoryOption? _selectedCategory;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<EventDataRow> _visibleRows = [];
    [ObservableProperty] private EventDataRow? _selectedRow;
    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private IReadOnlyList<EventDifferenceRow> _differences = [];
    [ObservableProperty] private bool _isComparing;

    /// <summary>
    /// Rows Core has no name for show only as numbers; on BDSP that is all but about 2% of them.
    /// They stay hidden unless asked for (or unless the bank has no names at all).
    /// </summary>
    [ObservableProperty] private bool _showUnnamed;

    private int UnnamedCount => _rows.Count(r => r.Kind == SelectedBank.Kind && !r.IsNamed);
    private bool BankHasNames => _rows.Any(r => r.Kind == SelectedBank.Kind && r.IsNamed);
    public bool CanHideUnnamed => BankHasNames && UnnamedCount != 0;
    public string ShowUnnamedText => LocalizedStrings.Instance.Format("Events_ShowUnnamed", UnnamedCount);

    /// <summary>True when some names carry a description decoded from the internal name (in [brackets]).</summary>
    public bool HasDecodedNames => _rows.Any(r => r.Name.Contains(" [", StringComparison.Ordinal));
    public int MaxIndex => _rows.Count(r => r.Kind == SelectedBank.Kind) - 1;
    public bool CanApply => !_rows.Any(r => r.HasErrors);
    public bool CanCompare => !IsComparing;
    public string ScopeText => LocalizedStrings.Instance.Format("Events_Scope", VisibleRows.Count, MaxIndex + 1);
    partial void OnSelectedBankChanged(EventBankOption value)
    {
        BuildCategories();
        Filter();
        SelectedIndex = 0;
        SelectedRow = _rows.First(r => r.Kind == value.Kind && r.Index == 0);
        OnPropertyChanged(nameof(MaxIndex));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(CanHideUnnamed));
        OnPropertyChanged(nameof(ShowUnnamedText));
    }
    partial void OnShowUnnamedChanged(bool value) => Filter();
    private void BuildCategories()
    {
        Categories = new[] { new EventCategoryOption(string.Empty) }.Concat(_rows.Where(r => r.Kind == SelectedBank.Kind)
            .Select(r => r.CategoryKey).Distinct().OrderBy(k => k)
            .Select(k => new EventCategoryOption(k))).ToArray();
        SelectedCategory = Categories[0];
    }
    partial void OnSelectedCategoryChanged(EventCategoryOption? value) => Filter();
    partial void OnSearchTextChanged(string value) => Filter();
    private void Filter()
    {
        var includeUnnamed = ShowUnnamed || !CanHideUnnamed;
        VisibleRows = _rows.Where(r => r.Kind == SelectedBank.Kind
            && (includeUnnamed || r.IsNamed)
            && (string.IsNullOrEmpty(SelectedCategory?.Key) || r.CategoryKey == SelectedCategory.Key)
            && (string.IsNullOrWhiteSpace(SearchText) || r.Matches(SearchText))).ToArray();
        OnPropertyChanged(nameof(ScopeText));
    }
    partial void OnSelectedIndexChanged(int value)
    {
        if (value < 0 || value > MaxIndex)
        {
            SelectedIndex = Math.Clamp(value, 0, MaxIndex);
            return;
        }
        var row = _rows.First(r => r.Kind == SelectedBank.Kind && r.Index == value);
        if (!VisibleRows.Contains(row))
        {
            SearchText = string.Empty;
            SelectedCategory = Categories[0];
            if (!row.IsNamed)
                ShowUnnamed = true;
        }
        SelectedRow = row;
    }
    partial void OnSelectedRowChanged(EventDataRow? value)
    {
        if (value is not null && value.Kind == SelectedBank.Kind)
            SelectedIndex = value.Index;
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EventDataRow.HasErrors) or nameof(EventDataRow.ValueText) or nameof(EventDataRow.IsSet))
        {
            OnPropertyChanged(nameof(CanApply));
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }
    partial void OnIsComparingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCompare));
        CompareCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (!CanApply) return;
        var count = _session.Commit();
        foreach (var row in _rows) row.RefreshValues();
        StatusText = LocalizedStrings.Instance.Format("Events_Applied", count);
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Reset()
    {
        _session.Reset();
        foreach (var row in _rows) row.RefreshValues();
        StatusText = LocalizedStrings.Instance["Events_Discarded"];
    }
    [RelayCommand] private void Cancel()
    {
        Reset();
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void ClearResearch() => Differences = [];
    public bool CompareSaves(SaveFile older, SaveFile newer)
    {
        if (!EventDataSession.TryCompare(older, newer, out var differences))
        {
            Differences = [];
            StatusText = LocalizedStrings.Instance["Events_Incompatible"];
            return false;
        }
        Differences = differences.Select(d => new EventDifferenceRow(d)).ToArray();
        StatusText = LocalizedStrings.Instance.Format("Events_Differences", Differences.Count);
        return true;
    }
    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        IsComparing = true;
        try
        {
            var olderPath = await _dialogs.OpenFileAsync(LocalizedStrings.Instance["Events_OlderSave"], ["*.sav", "*.main", "*.bin", "*", "*.*"]);
            if (olderPath is null) return;
            var newerPath = await _dialogs.OpenFileAsync(LocalizedStrings.Instance["Events_NewerSave"], ["*.sav", "*.main", "*.bin", "*", "*.*"]);
            if (newerPath is null) return;
            var older = await ReadSave(olderPath);
            var newer = await ReadSave(newerPath);
            if (older is null || newer is null || !CompareSaves(older, newer))
            {
                Differences = [];
                StatusText = LocalizedStrings.Instance["Events_Incompatible"];
                await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Events_Research"], StatusText);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Differences = [];
            StatusText = LocalizedStrings.Instance["Events_ReadFailed"];
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Events_Research"], StatusText);
        }
        finally { IsComparing = false; }
    }
    private static async Task<SaveFile?> ReadSave(string path)
    {
        // Bound reads before parsing; keep files, paths, and raw contents local to this call.
        var info = new FileInfo(path);
        if (info.Length > 32 * 1024 * 1024) return null;
        var data = await File.ReadAllBytesAsync(path);
        return FileUtil.GetSupportedFile(data, Path.GetFileName(path)) as SaveFile;
    }
    private void RefreshLanguage()
    {
        var category = SelectedCategory?.Key;
        _session.RefreshLabels();
        foreach (var bank in Banks) bank.RefreshLanguage();
        foreach (var row in _rows) row.RefreshLanguage();
        BuildCategories();
        SelectedCategory = Categories.FirstOrDefault(c => c.Key == category) ?? Categories[0];
        Filter();
        OnPropertyChanged(nameof(CanHideUnnamed));
        OnPropertyChanged(nameof(ShowUnnamedText));
        foreach (var row in Differences) row.RefreshLanguage();
    }
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        foreach (var row in _rows) row.PropertyChanged -= RowChanged;
    }
}

public sealed class EventBankOption(EventDataKind kind) : ObservableObject
{
    public EventDataKind Kind { get; } = kind;
    public string Name => LocalizedStrings.Instance[$"Events_Bank_{Kind}"];
    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));
}
public sealed class EventCategoryOption(string key) : ObservableObject
{
    public string Key { get; } = key;
    public string Name => LocalizedStrings.Instance[string.IsNullOrEmpty(Key) ? "Events_AllCategories" : $"Events_Category_{Key}"];
}
public sealed class EventDifferenceRow(EventDataDifference difference) : ObservableObject
{
    public string Kind => LocalizedStrings.Instance[$"Events_Bank_{difference.Kind}"];
    public int Index => difference.Index;
    public string Name => string.IsNullOrEmpty(difference.Name) ? LocalizedStrings.Instance.Format("Events_RawEntry", Index) : difference.Name;
    public long Before => difference.Before;
    public long After => difference.After;
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Name));
    }
}

public partial class EventDataRow : ObservableObject, INotifyDataErrorInfo
{
    private readonly EventDataField _field;
    private bool _refreshing;
    public EventDataRow(EventDataField field)
    {
        _field = field;
        _valueText = field.Value.ToString(CultureInfo.InvariantCulture);
        _floatText = FloatDisplay;
    }
    public EventDataKind Kind => _field.Kind;
    public int Index => _field.Index;
    public string HexIndex => $"0x{Index:X4}";
    public string Name => string.IsNullOrEmpty(_field.Name) ? LocalizedStrings.Instance.Format("Events_RawEntry", Index) : _field.Name;
    /// <summary>Core has a label for this slot: a name, or at least a category.</summary>
    public bool IsNamed => !string.IsNullOrEmpty(_field.Name) || _field.Category != "None";
    public string CategoryKey => _field.Category;
    public string Category => LocalizedStrings.Instance[$"Events_Category_{CategoryKey}"];
    public bool IsBoolean => _field.IsBoolean;
    public bool SupportsFloat => _field.SupportsFloatInterpretation;
    public IReadOnlyList<EventDataOption> Options => _field.Options;
    public bool HasOptions => Options.Count != 0;
    public string BoundsText => LocalizedStrings.Instance.Format("Events_Bounds", _field.Minimum, _field.Maximum);
    public string DisplayValue => IsBoolean ? LocalizedStrings.Instance[IsSet ? "Events_On" : "Events_Off"] : _field.Value.ToString(CultureInfo.InvariantCulture);
    public bool IsSet
    {
        get => _field.Value != 0;
        set
        {
            if (!IsBoolean) return;
            _field.TrySetValue(value ? 1 : 0);
            RefreshValues();
        }
    }
    [ObservableProperty] private string _valueText;
    [ObservableProperty] private string _floatText;
    [ObservableProperty] private bool _editAsFloat;
    [ObservableProperty] private EventDataOption? _selectedPreset;
    [ObservableProperty] private string _errorText = string.Empty;
    public bool HasErrors => ErrorText.Length != 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => HasErrors && (string.IsNullOrEmpty(propertyName)
        || propertyName is nameof(ValueText) or nameof(FloatText)) ? new[] { ErrorText } : Array.Empty<string>();
    public bool Matches(string query) => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Index.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)
        || HexIndex.Contains(query, StringComparison.OrdinalIgnoreCase)
        || DisplayValue.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    partial void OnSelectedPresetChanged(EventDataOption? value)
    {
        if (value is not null) ValueText = value.Value.ToString(CultureInfo.InvariantCulture);
    }
    partial void OnValueTextChanged(string value)
    {
        if (_refreshing) return;
        if (!TryParse(value, out var number) || !_field.TrySetValue(number))
        {
            ErrorText = LocalizedStrings.Instance["Events_InvalidValue"];
            return;
        }
        ErrorText = string.Empty;
        _floatText = FloatDisplay;
        OnPropertyChanged(nameof(FloatText));
        OnPropertyChanged(nameof(DisplayValue));
    }
    private bool TryParse(string text, out long value)
    {
        text = text.Trim();
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        value = 0;
        if (!ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var raw)) return false;
        if (_field.Minimum == int.MinValue && raw <= uint.MaxValue)
            value = unchecked((int)raw);
        else if (raw <= long.MaxValue) value = (long)raw;
        else return false;
        return true;
    }
    private string FloatDisplay => SupportsFloat
        ? BitConverter.Int32BitsToSingle((int)_field.Value).ToString("R", CultureInfo.InvariantCulture) : string.Empty;
    partial void OnEditAsFloatChanged(bool value)
    {
        // Changing interpretation abandons an invalid text entry without changing its stored bits.
        RefreshValues();
    }
    partial void OnFloatTextChanged(string value)
    {
        if (_refreshing || !SupportsFloat) return;
        if ((!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && !float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) || !float.IsFinite(number))
        {
            ErrorText = LocalizedStrings.Instance["Events_InvalidFloat"];
            return;
        }
        _field.TrySetValue(BitConverter.SingleToInt32Bits(number));
        ErrorText = string.Empty;
        _valueText = _field.Value.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(DisplayValue));
    }
    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(ValueText)));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(FloatText)));
    }
    public void RefreshValues()
    {
        _refreshing = true;
        try
        {
            ValueText = _field.Value.ToString(CultureInfo.InvariantCulture);
            FloatText = FloatDisplay;
            SelectedPreset = null;
            ErrorText = string.Empty;
            OnPropertyChanged(nameof(IsSet));
            OnPropertyChanged(nameof(DisplayValue));
        }
        finally { _refreshing = false; }
    }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(BoundsText));
        OnPropertyChanged(nameof(DisplayValue));
        if (HasErrors) ErrorText = LocalizedStrings.Instance[EditAsFloat ? "Events_InvalidFloat" : "Events_InvalidValue"];
    }
}
