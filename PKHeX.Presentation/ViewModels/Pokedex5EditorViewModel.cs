using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using System.Globalization;

namespace PKHeX.Presentation.ViewModels;

public partial class Pokedex5EditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SAV5? _source;
    private readonly SAV5? _sav;
    private bool _loading;
    private ushort _loadedSpecies;
    private readonly Zukan5? _zukan;

    public Pokedex5EditorViewModel(SaveFile sav)
    {
        _source = sav as SAV5;
        if (_source is not null)
        {
            var edited = _source.State.Edited;
            try { _sav = (SAV5)_source.Clone(); }
            finally { _source.State.Edited = edited; }
        }
        _zukan = _sav?.Zukan;
        IsSupported = _sav is not null && _zukan is not null;

        if (IsSupported)
        {
            var speciesList = GameInfo.Strings.Species;
            Species = new ObservableCollection<ComboItem>(
                Enumerable.Range(1, _sav!.MaxSpeciesID)
                    .Select(i => new ComboItem(speciesList[i], i)));

            _selectedSpecies = Species.FirstOrDefault(s => s.Value == 1) ?? Species.FirstOrDefault();
            LoadGlobals();
            if (_selectedSpecies != null)
                LoadEntry((ushort)_selectedSpecies.Value);
        }
    }

    public bool IsSupported { get; }
    public ObservableCollection<ComboItem> Species { get; } = [];

    [ObservableProperty]
    private ComboItem? _selectedSpecies;

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var match = Species.FirstOrDefault(s => s.Text.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectedSpecies = match;
    }

    partial void OnSelectedSpeciesChanged(ComboItem? value)
    {
        if (value is not null) LoadEntry((ushort)value.Value);
    }

    partial void OnCaughtChanged(bool value) => StoreEntry();
    partial void OnSeenMaleChanged(bool value) => StoreEntry();
    partial void OnSeenFemaleChanged(bool value) => StoreEntry();
    partial void OnSeenMaleShinyChanged(bool value) => StoreEntry();
    partial void OnSeenFemaleShinyChanged(bool value) => StoreEntry();
    partial void OnDisplayedMaleChanged(bool value) => SetDisplayedChoice(0, value);
    partial void OnDisplayedFemaleChanged(bool value) => SetDisplayedChoice(1, value);
    partial void OnDisplayedMaleShinyChanged(bool value) => SetDisplayedChoice(2, value);
    partial void OnDisplayedFemaleShinyChanged(bool value) => SetDisplayedChoice(3, value);
    partial void OnLangJPNChanged(bool value) => StoreEntry();
    partial void OnLangENGChanged(bool value) => StoreEntry();
    partial void OnLangFRAChanged(bool value) => StoreEntry();
    partial void OnLangGERChanged(bool value) => StoreEntry();
    partial void OnLangITAChanged(bool value) => StoreEntry();
    partial void OnLangSPAChanged(bool value) => StoreEntry();
    partial void OnLangKORChanged(bool value) => StoreEntry();

    private void SetDisplayedChoice(int region, bool value)
    {
        if (_loading) return;
        if (value)
        {
            _loading = true;
            DisplayedMale = region == 0;
            DisplayedFemale = region == 1;
            DisplayedMaleShiny = region == 2;
            DisplayedFemaleShiny = region == 3;
            _loading = false;
        }
        StoreEntry();
    }

    // Global Settings
    [ObservableProperty] private bool _nationalDexUnlocked;
    [ObservableProperty] private bool _nationalDexMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _spindaPID = "00000000";
    public bool CanSave => IsSupported && uint.TryParse(SpindaPID, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    public Action? CloseRequested { get; set; }

    partial void OnNationalDexUnlockedChanged(bool value)
    {
        if (_loading || _zukan is null) return;
        _zukan.IsNationalDexUnlocked = value;
        if (!value) NationalDexMode = false;
    }
    partial void OnNationalDexModeChanged(bool value)
    {
        if (!_loading && _zukan is not null) _zukan.IsNationalDexMode = value;
    }
    partial void OnSpindaPIDChanged(string value)
    {
        if (!_loading && _zukan is not null && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid))
            _zukan.Spinda = pid;
    }

    // Entry Data
    [ObservableProperty] private bool _caught;

    // Seen flags (Male, Female, Male Shiny, Female Shiny)
    [ObservableProperty] private bool _seenMale;
    [ObservableProperty] private bool _seenFemale;
    [ObservableProperty] private bool _seenMaleShiny;
    [ObservableProperty] private bool _seenFemaleShiny;

    // Displayed flags
    [ObservableProperty] private bool _displayedMale;
    [ObservableProperty] private bool _displayedFemale;
    [ObservableProperty] private bool _displayedMaleShiny;
    [ObservableProperty] private bool _displayedFemaleShiny;

    // Gender availability
    [ObservableProperty] private bool _canBeMale;
    [ObservableProperty] private bool _canBeFemale;

    // Language flags (only for species 1-493)
    public bool HasLanguages => SelectedSpecies != null && SelectedSpecies.Value <= 493;

    [ObservableProperty] private bool _langJPN;
    [ObservableProperty] private bool _langENG;
    [ObservableProperty] private bool _langFRA;
    [ObservableProperty] private bool _langGER;
    [ObservableProperty] private bool _langITA;
    [ObservableProperty] private bool _langSPA;
    [ObservableProperty] private bool _langKOR;

    // Forms
    public bool HasForms => FormsSeen.Count > 0;
    public ObservableCollection<FormFlagViewModel> FormsSeen { get; } = [];
    public ObservableCollection<FormFlagViewModel> FormsDisplayed { get; } = [];

    private void LoadEntry(ushort species)
    {
        if (_zukan is null || _sav is null) return;

        _loading = true;
        _loadedSpecies = species;
        Caught = _zukan.GetCaught(species);

        // Seen: regions 0=Male, 1=Female, 2=MaleShiny, 3=FemaleShiny
        SeenMale = _zukan.GetSeen(species, 0);
        SeenFemale = _zukan.GetSeen(species, 1);
        SeenMaleShiny = _zukan.GetSeen(species, 2);
        SeenFemaleShiny = _zukan.GetSeen(species, 3);

        // Displayed
        DisplayedMale = _zukan.GetDisplayed(species, 0);
        DisplayedFemale = _zukan.GetDisplayed(species, 1);
        DisplayedMaleShiny = _zukan.GetDisplayed(species, 2);
        DisplayedFemaleShiny = _zukan.GetDisplayed(species, 3);

        // Gender availability
        var pi = _sav.Personal[species];
        CanBeMale = !pi.OnlyFemale;
        CanBeFemale = !(pi.OnlyMale || pi.Genderless);

        // Languages
        LoadLanguages(species);

        // Forms
        LoadForms(species);

        OnPropertyChanged(nameof(HasLanguages));
        OnPropertyChanged(nameof(HasForms));
        _loading = false;
    }

    private void LoadGlobals()
    {
        if (_zukan is null) return;
        _loading = true;
        NationalDexUnlocked = _zukan.IsNationalDexUnlocked;
        NationalDexMode = _zukan.IsNationalDexMode;
        SpindaPID = _zukan.Spinda.ToString("X8", CultureInfo.InvariantCulture);
        _loading = false;
    }

    private void LoadLanguages(ushort species)
    {
        if (_zukan is null) return;

        if (species <= 493)
        {
            LangJPN = _zukan.GetLanguageFlag(species, 0);
            LangENG = _zukan.GetLanguageFlag(species, 1);
            LangFRA = _zukan.GetLanguageFlag(species, 2);
            LangGER = _zukan.GetLanguageFlag(species, 3);
            LangITA = _zukan.GetLanguageFlag(species, 4);
            LangSPA = _zukan.GetLanguageFlag(species, 5);
            LangKOR = _zukan.GetLanguageFlag(species, 6);
        }
        else
        {
            LangJPN = LangENG = LangFRA = LangGER = LangITA = LangSPA = LangKOR = false;
        }
    }

    private void LoadForms(ushort species)
    {
        foreach (var row in FormsSeen.Concat(FormsDisplayed)) row.PropertyChanged -= FormChanged;
        FormsSeen.Clear();
        FormsDisplayed.Clear();

        if (_zukan is null || _sav is null) return;

        var (index, count) = _zukan.GetFormIndex(species);
        if (count == 0) return;

        var formNames = FormConverter.GetFormList(species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolUnicode, _sav.Context);
        for (int i = 0; i < count; i++)
        {
            var name = i < formNames.Length ? formNames[i] : i.ToString(CultureInfo.InvariantCulture);
            FormsSeen.Add(new FormFlagViewModel(name, false, _zukan.GetFormFlag(index + i, 0)));
            FormsSeen.Add(new FormFlagViewModel(name, true, _zukan.GetFormFlag(index + i, 1)));

            FormsDisplayed.Add(new FormFlagViewModel(name, false, _zukan.GetFormFlag(index + i, 2)));
            FormsDisplayed.Add(new FormFlagViewModel(name, true, _zukan.GetFormFlag(index + i, 3)));
        }
        foreach (var row in FormsSeen.Concat(FormsDisplayed)) row.PropertyChanged += FormChanged;
    }

    private void FormChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName != nameof(FormFlagViewModel.IsChecked)) return;
        if (sender is FormFlagViewModel row && row.IsChecked && FormsDisplayed.Contains(row))
        {
            _loading = true;
            foreach (var other in FormsDisplayed) if (!ReferenceEquals(other, row)) other.IsChecked = false;
            _loading = false;
        }
        StoreEntry();
    }

    private void StoreEntry()
    {
        if (_loading || _zukan is null || _sav is null || _loadedSpecies == 0) return;
        var species = _loadedSpecies;

        _zukan.SetCaught(species, Caught);

        _zukan.SetSeen(species, 0, SeenMale);
        _zukan.SetSeen(species, 1, SeenFemale);
        _zukan.SetSeen(species, 2, SeenMaleShiny);
        _zukan.SetSeen(species, 3, SeenFemaleShiny);

        _zukan.SetDisplayed(species, 0, DisplayedMale);
        _zukan.SetDisplayed(species, 1, DisplayedFemale);
        _zukan.SetDisplayed(species, 2, DisplayedMaleShiny);
        _zukan.SetDisplayed(species, 3, DisplayedFemaleShiny);

        // Languages
        if (species <= 493)
        {
            _zukan.SetLanguageFlag(species, 0, LangJPN);
            _zukan.SetLanguageFlag(species, 1, LangENG);
            _zukan.SetLanguageFlag(species, 2, LangFRA);
            _zukan.SetLanguageFlag(species, 3, LangGER);
            _zukan.SetLanguageFlag(species, 4, LangITA);
            _zukan.SetLanguageFlag(species, 5, LangSPA);
            _zukan.SetLanguageFlag(species, 6, LangKOR);
        }

        // Forms
        var (index, count) = _zukan.GetFormIndex(species);
        if (count > 0)
        {
            int formCount = FormsSeen.Count / 2;
            for (int i = 0; i < formCount; i++)
            {
                _zukan.SetFormFlag(index + i, 0, FormsSeen[2 * i].IsChecked);
                _zukan.SetFormFlag(index + i, 1, FormsSeen[2 * i + 1].IsChecked);
                _zukan.SetFormFlag(index + i, 2, FormsDisplayed[2 * i].IsChecked);
                _zukan.SetFormFlag(index + i, 3, FormsDisplayed[2 * i + 1].IsChecked);
            }
        }

    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave || _source is null || _zukan is null) return;
        StoreEntry();
        if (!_source.Zukan.Data.SequenceEqual(_zukan.Data))
        {
            _zukan.Data.CopyTo(_source.Zukan.Data);
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
    [RelayCommand] private void Reset()
    {
        if (_source is null || _zukan is null) return;
        _source.Zukan.Data.CopyTo(_zukan.Data);
        LoadGlobals();
        if (SelectedSpecies is not null) LoadEntry((ushort)SelectedSpecies.Value);
    }

    [ObservableProperty] private bool _wholeDex = true;
    [ObservableProperty] private bool _bulkMale = true;
    [ObservableProperty] private bool _bulkFemale = true;
    [ObservableProperty] private bool _bulkMaleShiny;
    [ObservableProperty] private bool _bulkFemaleShiny;
    [ObservableProperty] private bool _bulkRegularForms = true;
    [ObservableProperty] private bool _bulkShinyForms;
    [ObservableProperty] private int _bulkLanguage = -1;
    public IReadOnlyList<ComboItem> BulkLanguages { get; } = new[] { new ComboItem(LocalizedStrings.Instance["Dex5Bulk_AllLanguages"], -1) }
        .Concat(new[] { "Japanese", "English", "French", "German", "Italian", "Spanish", "Korean" }
            .Select((name, index) => new ComboItem(LocalizedStrings.Instance[$"Pokedex5Editor_Lang{name}"], index))).ToArray();
    public string ScopeText => LocalizedStrings.Instance.Format("Dex5Bulk_Scope", WholeDex ? Species.Count : 1);
    partial void OnWholeDexChanged(bool value) => OnPropertyChanged(nameof(ScopeText));
    private System.Collections.Generic.IEnumerable<ushort> Scope => WholeDex
        ? Species.Select(item => (ushort)item.Value) : new[] { (ushort)(SelectedSpecies?.Value ?? 1) };
    private void RefreshEntry() { if (SelectedSpecies is not null) LoadEntry((ushort)SelectedSpecies.Value); }
    [RelayCommand] private void SeenAll()
    {
        if (_zukan is null || _sav is null) return;
        foreach (var species in Scope)
        {
            var pi = _sav.Personal[species];
            if (!pi.OnlyFemale) _zukan.SetSeen(species, 0);
            if (!pi.OnlyMale && !pi.Genderless) _zukan.SetSeen(species, 1);
            if (!_zukan.GetDisplayedAny(species)) _zukan.SetDisplayed(species, pi.OnlyFemale ? 1 : 0);
        }
        RefreshEntry();
    }
    [RelayCommand] private void CaughtAll()
    {
        if (_zukan is null) return;
        foreach (var species in Scope) _zukan.SetCaught(species);
        SeenAll();
    }
    [RelayCommand] private void ClearEntries()
    {
        if (_zukan is null) return;
        foreach (var species in Scope)
        {
            _zukan.SetCaught(species, false);
            for (int region = 0; region < 4; region++)
            {
                _zukan.SetSeen(species, region, false);
                _zukan.SetDisplayed(species, region, false);
            }
            if (species <= 493) for (int language = 0; language < 7; language++) _zukan.SetLanguageFlag(species, language, false);
            var (index, count) = _zukan.GetFormIndex(species);
            for (int i = 0; i < count; i++) for (int region = 0; region < 4; region++) _zukan.SetFormFlag(index + i, region, false);
        }
        RefreshEntry();
    }
    [RelayCommand] private void ApplySeenSelection()
    {
        if (_zukan is null || _sav is null) return;
        foreach (var species in Scope)
        {
            var pi = _sav.Personal[species];
            bool[] seen = [BulkMale && !pi.OnlyFemale, BulkFemale && !pi.OnlyMale && !pi.Genderless,
                BulkMaleShiny && !pi.OnlyFemale, BulkFemaleShiny && !pi.OnlyMale && !pi.Genderless];
            int displayed = Array.IndexOf(seen, true);
            for (int region = 0; region < 4; region++)
            {
                _zukan.SetSeen(species, region, seen[region]);
                _zukan.SetDisplayed(species, region, region == displayed);
            }
        }
        RefreshEntry();
    }
    [RelayCommand] private void SetLanguages() => Languages(true);
    [RelayCommand] private void ClearLanguages() => Languages(false);
    private void Languages(bool value)
    {
        if (_zukan is null || BulkLanguage is < -1 or > 6) return;
        foreach (var species in Scope.Where(s => s <= 493))
            for (int language = 0; language < 7; language++)
                if (BulkLanguage == -1 || BulkLanguage == language) _zukan.SetLanguageFlag(species, language, value);
        RefreshEntry();
    }
    [RelayCommand] private void SetForms() => SetFormFlags(true);
    [RelayCommand] private void ClearForms() => SetFormFlags(false);
    private void SetFormFlags(bool value)
    {
        if (_zukan is null) return;
        foreach (var species in Scope)
        {
            var (index, count) = _zukan.GetFormIndex(species);
            for (int i = 0; i < count; i++)
            {
                _zukan.SetFormFlag(index + i, 0, value && BulkRegularForms);
                _zukan.SetFormFlag(index + i, 1, value && BulkShinyForms);
                _zukan.SetFormFlag(index + i, 2, value && BulkRegularForms && i == 0);
                _zukan.SetFormFlag(index + i, 3, value && !BulkRegularForms && BulkShinyForms && i == 0);
            }
        }
        RefreshEntry();
    }
}

public partial class FormFlagViewModel : ObservableObject
{
    public FormFlagViewModel(string name, bool isShiny, bool isChecked)
    {
        Name = isShiny ? $"★ {name}" : name;
        IsShiny = isShiny;
        _isChecked = isChecked;
    }

    public string Name { get; }
    public bool IsShiny { get; }

    [ObservableProperty]
    private bool _isChecked;
}
