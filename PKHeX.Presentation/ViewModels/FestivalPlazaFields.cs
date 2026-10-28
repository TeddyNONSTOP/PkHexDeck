using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class FestivalPlazaEditorViewModel
{
    public IReadOnlyList<PlazaNumberRow> Messages { get; private set; } = [];
    public IReadOnlyList<PlazaPhraseRow> Phrases { get; private set; } = [];
    public IReadOnlyList<PlazaNumberRow> Rewards { get; private set; } = [];
    public bool CanBulk => !_closed && IsSupported && _dialogs is not null;
    public bool CanDelete => CanBulk && SelectedFacility is not null;
    public bool CanSave => !_closed && IsSupported && TimestampError.Length == 0
        && CurrentFC is >= 0 and <= 9999999 && UsedFC >= 0 && UsedFC <= _session!.WorkingSave.GetRecordMax(38)
        && Rank is >= 0 and <= ushort.MaxValue && Messages.All(row => row.IsValid)
        && Rewards.All(row => row.IsValid) && Facilities.All(row => row.IsValid);
    public string ValidationSummary => CanSave || _closed ? string.Empty : LocalizedStrings.Instance["PlazaSession_Invalid"];
    [ObservableProperty] private string _timestamp = string.Empty;
    [ObservableProperty] private string _timestampError = string.Empty;
    [ObservableProperty] private int _selectedTab;

    partial void OnTimestampChanged(string value)
    {
        if (_loading || _closed || _festa is null) return;
        if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
        {
            TimestampError = string.Empty;
            _festa.FestaDate = date;
        }
        else TimestampError = LocalizedStrings.Instance["PlazaSession_TimestampError"];
        ValidationChanged();
    }
    private void LoadAdditionalFields()
    {
        if (_festa is null) return;
        try { Timestamp = _festa.FestaDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty; }
        catch (ArgumentOutOfRangeException) { Timestamp = string.Empty; }
        TimestampError = string.Empty;
        var festa = _festa;
        Messages = Enumerable.Range(0, 4).Select(i => new PlazaNumberRow($"PlazaSession_Message{i}",
            festa.GetFestaMessage(i), ushort.MaxValue, value => festa.SetFestaMessage(i, (ushort)value), ValidationChanged)).ToArray();
        // English/Japanese phrase labels and ordering follow upstream SAV_FestivalPlaza (GPL-3.0).
        Phrases = Enumerable.Range(0, 107).Select(i => new PlazaPhraseRow(i, festa.GetFestaPhraseUnlocked(i),
            value => festa.SetFestaPhraseUnlocked(i, value))).ToArray();
        Rewards = Enumerable.Range(0, 11).Select(i => new PlazaNumberRow($"PlazaSession_Reward{i}",
            festa.GetFestPrizeReceived(i), byte.MaxValue, value => festa.SetFestaPrizeReceived(i, (byte)value), ValidationChanged,
            PlazaCatalog.RewardChoices(festa.GetFestPrizeReceived(i)))).ToArray();
        OnPropertyChanged(nameof(Messages)); OnPropertyChanged(nameof(Phrases)); OnPropertyChanged(nameof(Rewards));
    }
    private void ValidationChanged()
    {
        OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(ValidationSummary));
        SaveCommand.NotifyCanExecuteChanged();
        UnlockAllPhrasesCommand.NotifyCanExecuteChanged(); UnlockAllRewardsCommand.NotifyCanExecuteChanged();
        DeleteVisitorCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanBulk))] private async Task UnlockAllPhrases()
    {
        if (_closed || _dialogs is null || !await Confirm("PlazaSession_UnlockPhrasesConfirm")) return;
        foreach (var row in Phrases) row.Unlocked = true;
    }
    [RelayCommand(CanExecute = nameof(CanBulk))] private async Task UnlockAllRewards()
    {
        if (_closed || _dialogs is null || !await Confirm("PlazaSession_UnlockRewardsConfirm")) return;
        foreach (var row in Rewards.Where(row => row.Value == 0)) row.Value = 1; // Unlocked, awaiting collection; do not mark rewards received.
    }
    [RelayCommand(CanExecute = nameof(CanDelete))] private async Task DeleteVisitor()
    {
        var selected = SelectedFacility;
        if (!CanDelete || selected is null || !await Confirm("PlazaSession_DeleteConfirm") || _closed) return;
        selected.DeleteVisitor();
    }
    partial void OnSelectedFacilityChanged(FacilityViewModel? value) => DeleteVisitorCommand.NotifyCanExecuteChanged();
    private Task<bool> Confirm(string key) => _dialogs!.ShowConfirmationAsync(
        LocalizedStrings.Instance["FestivalPlazaEditor_Title"], LocalizedStrings.Instance[key],
        LocalizedStrings.Instance["Common_OK"], LocalizedStrings.Instance["Common_Cancel"]);
}

public sealed partial class PlazaNumberRow : ObservableObject
{
    private readonly string _key;
    private readonly Action<long> _write;
    private readonly Action _changed;
    public long Maximum { get; }
    public string Name => LocalizedStrings.Instance[_key];
    public bool IsValid => Value >= 0 && Value <= Maximum;
    public IReadOnlyList<ComboItem> Choices { get; private set; }
    public PlazaNumberRow(string key, long value, long maximum, Action<long> write, Action changed, IReadOnlyList<ComboItem>? choices = null)
    { _key = key; _value = value; Maximum = maximum; _write = write; _changed = changed; Choices = choices ?? []; }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        if (Choices.Count != 0) { Choices = PlazaCatalog.RewardChoices(ChoiceValue); OnPropertyChanged(nameof(Choices)); }
    }
    [ObservableProperty] private long _value;
    // Choice IDs remain int to match ComboItem.Value; numeric rows support the full uint storage range.
    public int ChoiceValue { get => (int)Value; set => Value = value; }
    partial void OnValueChanged(long value)
    {
        if (IsValid) _write(value);
        OnPropertyChanged(nameof(ChoiceValue)); OnPropertyChanged(nameof(IsValid)); _changed();
    }
}

public sealed partial class PlazaPhraseRow : ObservableObject
{
    private readonly Action<bool> _write;
    public int Index { get; }
    public string Name => LocalizedStrings.Instance[$"PlazaSession_Phrase{Index}"];
    public PlazaPhraseRow(int index, bool unlocked, Action<bool> write) { Index = index; _unlocked = unlocked; _write = write; }
    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));
    [ObservableProperty] private bool _unlocked;
    partial void OnUnlockedChanged(bool value) => _write(value);
}
