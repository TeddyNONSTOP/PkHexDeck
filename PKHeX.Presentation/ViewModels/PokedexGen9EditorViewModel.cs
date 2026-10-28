using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class PokedexGen9EditorViewModel : ViewModelBase, IDisposable
{
    private readonly SvPokedexDataSession _session;
    private SAV9SV _sav => _session.Staged;
    private Zukan9 _zukan => _sav.Blocks.Zukan;
    private ushort _loadedSpecies;
    private bool _closed;
    private int _generation;
    private int _epoch;
    private readonly IDialogService? _dialogs;
    private bool _refreshingLabels;
    [ObservableProperty] private bool _entirePokedex;
    [ObservableProperty] private bool _includeShiny;
    public bool CanUndo => !_closed && _session.CanUndo;
    private LegacySnapshot? _baseline;
    [ObservableProperty] private string _error = string.Empty;
    public bool UsesDlcFormat => _session.UsesDlcFormat;
    public bool UsesLegacyFormat => !UsesDlcFormat;
    public bool HasError => Error.Length != 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    public bool CanSave => !_closed && RegionalDisplays.All(display => display.IsValid) && (_baseline is null ||
        (DisplayGender == _baseline.Gender || DisplayGender is >= 0 and <= 2) &&
        (DisplayForm == _baseline.Form || DisplayForm >= 0 && DisplayForm < Forms.Count));
    public IReadOnlyList<ComboItem> GenderChoices => GenderOptions(DisplayGender);
    internal static IReadOnlyList<ComboItem> GenderOptions(int selected)
    {
        var result = new List<ComboItem> { new(Localization.LocalizedStrings.Instance["PokedexGen9Editor_Male"], 0), new(Localization.LocalizedStrings.Instance["PokedexGen9Editor_Female"], 1), new(Localization.LocalizedStrings.Instance["PokedexGen9Editor_Genderless"], 2) };
        if (selected is < 0 or > 2) result.Add(new(Localization.LocalizedStrings.Instance.Format("RaidSession_UnknownType", selected), selected));
        return result;
    }
    public IReadOnlyList<ComboItem> FormChoices
    {
        get
        {
            var result = Forms.Select((name, index) => new ComboItem(name.Length == 0 ? Localization.LocalizedStrings.Instance["Dex9a_BaseForm"] : name, index)).ToList();
            if (result.All(item => item.Value != DisplayForm)) result.Add(new(Localization.LocalizedStrings.Instance.Format("RaidSession_UnknownType", unchecked((uint)DisplayForm)), DisplayForm));
            return result;
        }
    }
    
    public ObservableCollection<ComboItem> SpeciesList { get; } = [];
    public ObservableCollection<ComboItem> FilteredSpeciesList { get; private set; } = [];

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                FilterList();
        }
    }

    private ComboItem? _selectedSpecies;
    public ComboItem? SelectedSpecies
    {
        get => _selectedSpecies;
        set
        {
            if (_refreshingLabels) { SetProperty(ref _selectedSpecies, value); return; }
            if (_selectedSpecies is not null && _selectedSpecies != value && !CanSave)
            { Error = Localization.LocalizedStrings.Instance["Trainer7_InvalidValues"]; OnPropertyChanged(nameof(SelectedSpecies)); return; }
            if (_selectedSpecies is not null && _selectedSpecies != value) FlushLegacy();
            if (SetProperty(ref _selectedSpecies, value))
                LoadEntry();
        }
    }
    
    // Entry Properties
    [ObservableProperty] private bool _isCaught; // Derived or manually managed?
    [ObservableProperty] private bool _isSeenMale;
    [ObservableProperty] private bool _isSeenFemale;
    [ObservableProperty] private bool _isSeenGenderless;
    [ObservableProperty] private bool _isSeenShiny;
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private int _displayGender; // 0=M, 1=F, 2=G
    [ObservableProperty] private bool _displayShiny;
    [ObservableProperty] private int _displayForm;
    [ObservableProperty] private bool _displayGenderDiff;
    
    // Languages
    [ObservableProperty] private bool _langJPN;
    [ObservableProperty] private bool _langENG;
    [ObservableProperty] private bool _langFRE;
    [ObservableProperty] private bool _langITA;
    [ObservableProperty] private bool _langGER;
    [ObservableProperty] private bool _langSPA;
    [ObservableProperty] private bool _langKOR;
    [ObservableProperty] private bool _langCHS;
    [ObservableProperty] private bool _langCHT;

    public ObservableCollection<string> Forms { get; } = [];
    
    public PokedexGen9EditorViewModel(SAV9SV sav, IDialogService? dialogs = null)
    {
        _session = new(sav);
        _dialogs = dialogs;
        
        LoadSpeciesList();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((PokedexGen9EditorViewModel)recipient).RefreshLanguage());
    }

    private void LoadSpeciesList()
    {
        SpeciesList.Clear();
        foreach (ushort species in _session.Species().OrderBy(species => _session.Identifiers(species).Paldea == 0 ? 10000 + species : _session.Identifiers(species).Paldea))
        {
            var ids = _session.Identifiers(species);
            SpeciesList.Add(new ComboItem(FormatSpecies(species, ids), species));
        }
            
        FilterList();
        if (FilteredSpeciesList.Count > 0)
            SelectedSpecies = FilteredSpeciesList[0];
    }
    public void RefreshLanguage()
    {
        if (_closed) return;
        int? selected = SelectedSpecies?.Value;
        _refreshingLabels = true;
        try
        {
            var updated = SpeciesList.Select(choice =>
            {
                var ids = _session.Identifiers((ushort)choice.Value);
                return new ComboItem(FormatSpecies((ushort)choice.Value, ids), choice.Value);
            }).ToArray();
            SpeciesList.Clear(); foreach (var choice in updated) SpeciesList.Add(choice);
            FilterList(); SelectedSpecies = FilteredSpeciesList.FirstOrDefault(choice => choice.Value == selected) ?? FilteredSpeciesList.FirstOrDefault();
            if (_loadedSpecies != 0)
            {
                var names = FormConverter.GetFormList(_loadedSpecies, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9);
                Forms.Clear(); foreach (var name in names) Forms.Add(name);
                foreach (var form in FormStates) form.Name = form.Form < names.Length && names[form.Form].Length != 0 ? names[form.Form] : LocalizedStrings.Instance["Dex9a_BaseForm"];
                foreach (var display in RegionalDisplays) display.RefreshLabels(FormStates.Where(form => _session.IsRegionalFormSupported(_loadedSpecies, form.Form, display.Region)).Select(form => new ComboItem(form.Name, form.Form)));
            }
            int formValue = DisplayForm, genderValue = DisplayGender;
            OnPropertyChanged(nameof(FormChoices)); OnPropertyChanged(nameof(GenderChoices));
            DisplayForm = formValue; DisplayGender = genderValue;
        }
        finally { _refreshingLabels = false; }
    }
    private static string FormatSpecies(ushort species, (ushort Paldea, ushort Kitakami, ushort Blueberry) ids)
    {
        static string Number(ushort value) => value == 0 ? "—" : value.ToString("000");
        return $"{GameInfo.Strings.Species[species]}\nP:{Number(ids.Paldea)} K:{Number(ids.Kitakami)} B:{Number(ids.Blueberry)}";
    }

    private void FilterList()
    {
        var selected = SelectedSpecies;
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredSpeciesList = new ObservableCollection<ComboItem>(SpeciesList);
        }
        else
        {
            FilteredSpeciesList = new ObservableCollection<ComboItem>(
                SpeciesList.Where(x => x.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
        }
        OnPropertyChanged(nameof(FilteredSpeciesList));
        SelectedSpecies = selected is not null && FilteredSpeciesList.Contains(selected) ? selected : FilteredSpeciesList.FirstOrDefault();
    }

    private void LoadEntry()
    {
        if (SelectedSpecies is null) return;
        _generation++;
        
        ushort species = (ushort)SelectedSpecies.Value;
        _loadedSpecies = species;
        if (UsesDlcFormat) { LoadDlc(species); return; }
        
        var entry = _zukan.DexPaldea.Get(species);
        
        // Load Forms
        Forms.Clear();
        var formList = FormConverter.GetFormList(species, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9);
        foreach(var f in formList) Forms.Add(f);
        
        // Properties
        IsNew = entry.GetDisplayIsNew();
        IsCaught = entry.IsCaught;
        IsSeenMale = entry.GetIsGenderSeen(0);
        IsSeenFemale = entry.GetIsGenderSeen(1);
        IsSeenGenderless = entry.GetIsGenderSeen(2);
        IsSeenShiny = entry.GetSeenIsShiny();
        
        DisplayGender = (int)entry.GetDisplayGender();
        DisplayShiny = entry.GetDisplayIsShiny();
        DisplayGenderDiff = entry.GetDisplayGenderIsDifferent();
        DisplayForm = unchecked((int)entry.GetDisplayForm());

        // Languages
        LangJPN = entry.GetLanguageFlag((int)LanguageID.Japanese);
        LangENG = entry.GetLanguageFlag((int)LanguageID.English);
        LangFRE = entry.GetLanguageFlag((int)LanguageID.French);
        LangITA = entry.GetLanguageFlag((int)LanguageID.Italian);
        LangGER = entry.GetLanguageFlag((int)LanguageID.German);
        LangSPA = entry.GetLanguageFlag((int)LanguageID.Spanish);
        LangKOR = entry.GetLanguageFlag((int)LanguageID.Korean);
        LangCHS = entry.GetLanguageFlag((int)LanguageID.ChineseS);
        LangCHT = entry.GetLanguageFlag((int)LanguageID.ChineseT);
        _baseline = CaptureLegacy();
        OnPropertyChanged(nameof(FormChoices)); OnPropertyChanged(nameof(GenderChoices));
    }

    [RelayCommand]
    private void SaveCurrent()
    {
        if (!CanSave) { Error = Localization.LocalizedStrings.Instance["Trainer7_InvalidValues"]; return; }
        _epoch++;
        FlushLegacy();
        if (!_session.TryCommit()) { Error = Localization.LocalizedStrings.Instance["LgpeTrainer_Conflict"]; return; }
        Error = string.Empty; LoadEntry();
    }
    private void FlushLegacy()
    {
        if (_loadedSpecies == 0 || _baseline is null) return;
        if (UsesDlcFormat) { FlushDlcShared(); return; }
        var old = _baseline;
        ushort species = _loadedSpecies;
        var entry = _zukan.DexPaldea.Get(species);
        if (IsCaught != old.Caught) entry.SetCaught(IsCaught);
        if (IsNew != old.New) entry.SetDisplayIsNew(IsNew);
        if (IsSeenMale != old.Male) entry.SetIsGenderSeen(0, IsSeenMale);
        if (IsSeenFemale != old.Female) entry.SetIsGenderSeen(1, IsSeenFemale);
        if (IsSeenGenderless != old.Genderless) entry.SetIsGenderSeen(2, IsSeenGenderless);
        if (IsSeenShiny != old.SeenShiny) entry.SetSeenIsShiny(IsSeenShiny);
        if (DisplayGender != old.Gender) entry.SetDisplayGender(DisplayGender);
        if (DisplayShiny != old.Shiny) entry.SetDisplayIsShiny(DisplayShiny);
        if (DisplayGenderDiff != old.Different) entry.SetDisplayGenderIsDifferent(DisplayGenderDiff);
        if (DisplayForm != old.Form) entry.SetDisplayForm(unchecked((uint)DisplayForm));
        bool[] languages = [LangJPN, LangENG, LangFRE, LangITA, LangGER, LangSPA, LangKOR, LangCHS, LangCHT];
        int[] ids = [1, 2, 3, 4, 5, 7, 8, 9, 10];
        for (int i = 0; i < ids.Length; i++) if (languages[i] != old.Languages[i]) entry.SetLanguageFlag(ids[i], languages[i]);
        _baseline = CaptureLegacy();
    }

    // Batch commands (Apply to current only, or all?)
    // Typically WinForms has "Seen None/All" for CURRENT species, and "Modify All" menu for entire dex.
    
    [RelayCommand]
    private Task SeenAll() => BulkAsync(false);
    
    [RelayCommand]
    private Task CaughtAll() => BulkAsync(true);
    [RelayCommand] private Task Complete() => BulkAsync(true, true);
    private async Task BulkAsync(bool caught, bool complete = false)
    {
        if (_closed || !CanSave || _dialogs is null || SelectedSpecies is null) return;
        int epoch = _epoch, species = SelectedSpecies.Value; bool whole = EntirePokedex, shiny = IncludeShiny;
        string scope = whole ? Localization.LocalizedStrings.Instance["Dex9a_WholeDex"] : SelectedSpecies.Text;
        if (!await _dialogs.ShowConfirmationAsync(Localization.LocalizedStrings.Instance[complete ? "Dex9a_ActionComplete" : caught ? "Dex9a_ActionCaughtAll" : "Dex9a_ActionSeenAll"],
            Localization.LocalizedStrings.Instance.Format("Dex9a_Confirm", scope), Localization.LocalizedStrings.Instance["Dex9a_Apply"], Localization.LocalizedStrings.Instance["Common_Cancel"])) return;
        if (_closed || epoch != _epoch || whole != EntirePokedex || shiny != IncludeShiny || !whole && SelectedSpecies?.Value != species || !CanSave) return;
        FlushLegacy();
        _session.ApplyAction(save =>
        {
            foreach (ushort target in _session.Species().Where(target => whole || target == species))
            {
                int count = Math.Max(1, (int)save.Personal[target].FormCount);
                for (byte form = 0; form < Math.Min(count, 32); form++)
                {
                    if (!_session.IsFormSupported(target, form)) continue;
                    var state = _session.ReadForm(target, form);
                    if (UsesDlcFormat) _session.WriteForm(target, form, caught || state.Obtained, true, true, complete || state.Viewed);
                    else save.Blocks.Zukan.DexPaldea.Get(target).SetIsFormSeen(form, true);
                }
                if (!UsesDlcFormat)
                {
                    var entry = save.Blocks.Zukan.DexPaldea.Get(target); entry.SetSeen(true);
                    if (caught) entry.SetCaught(true);
                    if (shiny) entry.SetSeenIsShiny(true);
                }
                else if (shiny) save.Blocks.Zukan.DexKitakami.Get(target).SetIsModelSeen(true, true);
                if (UsesDlcFormat) save.Blocks.Zukan.DexKitakami.Get(target).SetIsModelSeen(false, true);
                if (complete)
                {
                    foreach (int language in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10 })
                    {
                        if (UsesDlcFormat) save.Blocks.Zukan.DexKitakami.Get(target).SetLanguageFlag(language, true);
                        else save.Blocks.Zukan.DexPaldea.Get(target).SetLanguageFlag(language, true);
                    }
                }
                byte genderRatio = save.Personal[target].Gender;
                foreach (byte gender in new byte[] { 0, 1, 2 })
                {
                    bool supported = genderRatio == 255 ? gender == 2 : genderRatio == 0 ? gender == 0 : genderRatio == 254 ? gender == 1 : gender < 2;
                    if (!supported) continue;
                    if (UsesDlcFormat) save.Blocks.Zukan.DexKitakami.Get(target).SetIsGenderSeen(gender, true);
                    else save.Blocks.Zukan.DexPaldea.Get(target).SetIsGenderSeen(gender, true);
                }
            }
        });
        _epoch++; LoadEntry(); OnPropertyChanged(nameof(CanUndo));
    }
    private LegacySnapshot CaptureLegacy() => new(IsCaught, IsNew, IsSeenMale, IsSeenFemale, IsSeenGenderless, IsSeenShiny,
        DisplayGender, DisplayShiny, DisplayGenderDiff, DisplayForm, [LangJPN, LangENG, LangFRE, LangITA, LangGER, LangSPA, LangKOR, LangCHS, LangCHT]);
    private sealed record LegacySnapshot(bool Caught, bool New, bool Male, bool Female, bool Genderless, bool SeenShiny,
        int Gender, bool Shiny, bool Different, int Form, bool[] Languages);
    [RelayCommand] private void Reset() { if (_closed) return; _epoch++; _session.Reset(); _baseline = null; _loadedSpecies = 0; LoadEntry(); Error = string.Empty; OnPropertyChanged(nameof(CanUndo)); }
    [RelayCommand] private void Undo() { if (_closed) return; _epoch++; _session.Undo(); LoadEntry(); OnPropertyChanged(nameof(CanUndo)); }
    public void Dispose() { _closed = true; _epoch++; _generation++; WeakReferenceMessenger.Default.UnregisterAll(this); }
}
