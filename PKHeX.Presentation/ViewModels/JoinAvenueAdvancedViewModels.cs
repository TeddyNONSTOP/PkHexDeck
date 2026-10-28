using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public abstract partial class JoinAvenueEntityViewModel
{
    public IReadOnlyList<JoinAvenueNumberField> AdvancedNumbers { get; private set; } = [];
    public IReadOnlyList<JoinAvenueFlagField> AdvancedFlags { get; private set; } = [];
    public IReadOnlyList<JoinAvenueDateField> AdvancedDates { get; private set; } = [];
    public bool HasAdvancedDates => AdvancedDates.Count != 0;
    private bool _advancedInitialized;
    private void RefreshAdvanced()
    {
        if (!_advancedInitialized)
        {
            (AdvancedNumbers, AdvancedFlags, AdvancedDates) = JoinAvenueAdvancedFields.Create(Entity, ApplyAdvanced);
            _advancedInitialized = true;
        }
        RefreshAdvancedValues();
    }
    protected void RefreshAdvancedValues()
    {
        foreach (var row in AdvancedNumbers) row.Refresh();
        foreach (var row in AdvancedFlags) row.Refresh();
        foreach (var row in AdvancedDates) row.Refresh();
    }
    private void ApplyAdvanced(Action apply)
    {
        if (Suppress || Parent.IsLoading) return;
        var before = Entity.Write().ToArray();
        apply();
        if (!Entity.Write().SequenceEqual(before)) Parent.MarkEdited();
        Reload();
    }
}

public sealed partial class JoinAvenueNumberField : ObservableObject
{
    private readonly Func<long> _read;
    private readonly Action<long> _write;
    private readonly string _labelKey;
    private readonly object? _labelArgument;
    private bool _refreshing;
    public string Id { get; }
    public string Name => _labelArgument is null ? LocalizedStrings.Instance[_labelKey] : LocalizedStrings.Instance.Format(_labelKey, _labelArgument);
    public long Maximum { get; }
    public string Error => Value >= 0 && Value <= Maximum ? string.Empty : LocalizedStrings.Instance.Format("JoinAvenueAdvanced_RangeError", Maximum);
    public JoinAvenueNumberField(string id, string labelKey, long maximum, Func<long> read, Action<long> write, object? labelArgument = null)
    { Id = id; _labelKey = labelKey; Maximum = maximum; _read = read; _write = write; _labelArgument = labelArgument; _value = read(); }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error))] private long _value;
    partial void OnValueChanged(long value) { if (!_refreshing && value >= 0 && value <= Maximum) _write(value); }
    public void Refresh() { _refreshing = true; Value = _read(); _refreshing = false; OnPropertyChanged(nameof(Name)); }
}

public sealed partial class JoinAvenueFlagField : ObservableObject
{
    private readonly Func<bool> _read;
    private readonly Action<bool> _write;
    private readonly string _labelKey;
    private readonly object? _labelArgument;
    private bool _refreshing;
    public string Id { get; }
    public string Name => _labelArgument is null ? LocalizedStrings.Instance[_labelKey] : LocalizedStrings.Instance.Format(_labelKey, _labelArgument);
    public JoinAvenueFlagField(string id, string labelKey, Func<bool> read, Action<bool> write, object? labelArgument = null)
    { Id = id; _labelKey = labelKey; _read = read; _write = write; _labelArgument = labelArgument; _value = read(); }
    [ObservableProperty] private bool _value;
    partial void OnValueChanged(bool value) { if (!_refreshing) _write(value); }
    public void Refresh() { _refreshing = true; Value = _read(); _refreshing = false; OnPropertyChanged(nameof(Name)); }
}

public sealed partial class JoinAvenueDateField : ObservableObject
{
    private readonly Func<JoinAvenueDate5> _read;
    private readonly Action<JoinAvenueDate5> _write;
    private readonly string _labelKey;
    private readonly object? _labelArgument;
    private bool _refreshing;
    public string Id { get; }
    public string Name => _labelArgument is null ? LocalizedStrings.Instance[_labelKey] : LocalizedStrings.Instance.Format(_labelKey, _labelArgument);
    public string RawName => $"{Name}: {LocalizedStrings.Instance["JoinAvenueAdvanced_RawDate"]}";
    public string ClearName => $"{Name}: {LocalizedStrings.Instance["JoinAvenueAdvanced_ClearDate"]}";
    public bool HasInvalidStoredDate => RawValue != 0 && new JoinAvenueDate5((ushort)RawValue).Date is null;
    public JoinAvenueDateField(string id, string labelKey, Func<JoinAvenueDate5> read, Action<JoinAvenueDate5> write, object? argument = null)
    { Id = id; _labelKey = labelKey; _read = read; _write = write; _labelArgument = argument; Refresh(); }
    [ObservableProperty] private long _rawValue;
    [ObservableProperty] private string _dateText = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    partial void OnRawValueChanged(long value)
    {
        if (_refreshing) return;
        if (value is < 0 or > ushort.MaxValue) { Error = LocalizedStrings.Instance.Format("JoinAvenueAdvanced_RangeError", ushort.MaxValue); return; }
        Error = string.Empty; _write(new JoinAvenueDate5((ushort)value));
    }
    partial void OnDateTextChanged(string value)
    {
        if (_refreshing) return;
        DateOnly? date = null;
        if (value.Length != 0)
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                || parsed.Year is < 2000 or > 2127) { Error = LocalizedStrings.Instance["JoinAvenueAdvanced_DateError"]; return; }
            date = parsed;
        }
        Error = string.Empty; _write(new JoinAvenueDate5 { Date = date });
    }
    [RelayCommand]
    private void Clear()
    {
        Error = string.Empty;
        if (_read().RawValue == 0) { Refresh(); return; }
        _write(new JoinAvenueDate5(0));
    }
    public void Refresh()
    {
        _refreshing = true; var date = _read(); var changed = RawValue != date.RawValue; RawValue = date.RawValue;
        if (changed || Error.Length == 0)
        {
            DateText = date.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            Error = string.Empty;
        }
        _refreshing = false; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(HasInvalidStoredDate)); OnPropertyChanged(nameof(RawName)); OnPropertyChanged(nameof(ClearName));
    }
}
