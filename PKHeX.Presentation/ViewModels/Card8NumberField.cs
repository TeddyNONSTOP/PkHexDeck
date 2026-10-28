using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Card8NumberField : ViewModelBase
{
    private readonly decimal _original, _validMin, _validMax;
    private readonly decimal? _sentinel;
    private readonly Action<decimal> _write;
    private readonly Action _changed;
    private bool _refreshing;
    public string Id { get; }
    public decimal Minimum { get; }
    public decimal Maximum { get; }
    public bool CanUnset => _sentinel.HasValue;
    public string Name => LocalizedStrings.Instance["Card8Flow_" + Id];
    [ObservableProperty] private decimal _value;
    [ObservableProperty] private bool _isUnset;
    public bool HasError => !IsUnset && Value != _original &&
        (Value != decimal.Truncate(Value) || Value < _validMin || Value > _validMax);
    public string Error => HasError ? LocalizedStrings.Instance.Format("Card8Flow_NumberError", _validMin, _validMax) : string.Empty;
    public bool HasStatus => !IsUnset && Status.Length != 0;
    public string Status => IsUnset ? LocalizedStrings.Instance["Card8Flow_NotSet"]
        : Value == _original && (Value < _validMin || Value > _validMax)
            ? LocalizedStrings.Instance.Format("Card8Flow_UnknownStored", Value) : string.Empty;

    public Card8NumberField(string id, decimal original, decimal minimum, decimal maximum,
        decimal? sentinel, decimal? validMax, decimal? validMin, Action<decimal> write, Action changed)
    {
        Id = id; _original = original; Minimum = minimum; Maximum = maximum;
        _sentinel = sentinel; _validMin = validMin ?? minimum; _validMax = validMax ?? maximum; _write = write; _changed = changed;
        _isUnset = sentinel == original; _value = _isUnset ? 0 : original;
    }
    partial void OnValueChanged(decimal value) => Apply();
    partial void OnIsUnsetChanged(bool value) => Apply();
    private void Apply()
    {
        if (_refreshing) return;
        if (!HasError) _write(IsUnset && _sentinel is { } sentinel ? sentinel : Value);
        RefreshLanguage(); _changed();
    }
    public void RefreshValue(decimal value)
    {
        _refreshing = true;
        IsUnset = _sentinel == value; Value = IsUnset ? 0 : value;
        _refreshing = false;
        RefreshLanguage();
    }
    public void RefreshLanguage()
    { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(HasStatus)); }
}
