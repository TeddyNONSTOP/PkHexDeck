using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Pokedex9aEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly Pokedex9aDataSession _session;
    private readonly IDialogService _dialogs;
    private List<ComboItem> _species = [];
    private bool _closed, _loading;
    public Action? CloseRequested { get; set; }
    public bool IsSupported { get; }
    public bool CanSave => !_closed && IsSupported && DisplayForm is >= 0 and <= 255 && DisplayGender is >= 0 and <= 255;
    public bool CanUndo => !_closed && _session.CanUndo;
    public ObservableCollection<Dex9aFormRow> Forms { get; } = [];
    public ObservableCollection<Dex9aBoolRow> Flags { get; } = [];
    public ObservableCollection<Dex9aBoolRow> Languages { get; } = [];
    public ObservableCollection<ComboItem> DisplayForms { get; } = [];
    public ObservableCollection<ComboItem> DisplayGenders { get; } = [];
    [ObservableProperty] private ObservableCollection<ComboItem> _filteredSpecies = [];
    [ObservableProperty] private ComboItem? _selectedSpecies;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private bool _includeShiny;
    [ObservableProperty] private bool _wholeDex;
    [ObservableProperty] private int _displayForm;
    [ObservableProperty] private int _displayGender;
    [ObservableProperty] private bool _displayShiny;
    [ObservableProperty] private bool _isNew;
    public bool HasError => Error.Length != 0;
    public ushort CurrentSpecies => (ushort)(SelectedSpecies?.Value ?? 0);

    public Pokedex9aEditorViewModel(SAV9ZA save, IDialogService dialogs)
    {
        _session = new(save); _dialogs = dialogs;
        int maxIndex = Enumerable.Range(0, save.MaxSpeciesID + 1).Max(id => SpeciesConverter.GetInternal9((ushort)id));
        IsSupported = _session.Dex.Data.Length >= (maxIndex + 1) * PokeDexEntry9a.SIZE;
        if (IsSupported) RebuildSpecies();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((Pokedex9aEditorViewModel)recipient).RefreshLanguage());
    }

    private string T(string key) => LocalizedStrings.Instance[key];
    private void RebuildSpecies()
    {
        int previous = SelectedSpecies?.Value ?? 0;
        _species = Enumerable.Range(1, _session.Staged.MaxSpeciesID)
            .Where(id => _session.Staged.Personal.IsSpeciesInGame((ushort)id))
            .Select(id => (Id: id, Dex: Pokedex9aCapabilities.GetDexIndex((ushort)id)))
            .OrderBy(item => item.Dex == 0 ? int.MaxValue : item.Dex).ThenBy(item => item.Id)
            .Select(item => new ComboItem(item.Dex == 0
                ? LocalizedStrings.Instance.Format("Dex9a_NotInDex", GameInfo.Strings.Species[item.Id])
                : $"{item.Dex:000} · {GameInfo.Strings.Species[item.Id]}", item.Id)).ToList();
        Filter(); SelectedSpecies = FilteredSpecies.FirstOrDefault(item => item.Value == previous) ?? FilteredSpecies.FirstOrDefault();
    }
    partial void OnSearchTextChanged(string value) => Filter();
    private void Filter()
    {
        int previous = SelectedSpecies?.Value ?? 0;
        FilteredSpecies = new(_species.Where(item => item.Text.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)));
        SelectedSpecies = FilteredSpecies.FirstOrDefault(item => item.Value == previous) ?? FilteredSpecies.FirstOrDefault();
    }
    partial void OnSelectedSpeciesChanged(ComboItem? value) { if (value is not null) LoadEntry(); }
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    private void LoadEntry()
    {
        if (_closed || CurrentSpecies == 0) return;
        _loading = true;
        try
        {
            ushort species = CurrentSpecies; var entry = _session.Dex.GetEntry(species);
            var names = FormConverter.GetFormList(species, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9a);
            Forms.Clear(); DisplayForms.Clear();
            for (byte form = 0; form < Math.Min(names.Length, 32); form++)
            {
                byte index = form; string name = names[form].Length == 0 ? T("Dex9a_BaseForm") : names[form];
                Forms.Add(new(name, entry.GetIsFormCaught(form), entry.GetIsFormSeen(form), entry.GetIsShinySeen(form),
                    value => { if (!_closed) _session.Dex.GetEntry(species).SetIsFormCaught(index, value); },
                    value => { if (!_closed) _session.Dex.GetEntry(species).SetIsFormSeen(index, value); },
                    value => { if (!_closed) _session.Dex.GetEntry(species).SetIsShinySeen(index, value); }));
                DisplayForms.Add(new(name, form));
            }
            Flags.Clear();
            for (byte gender = 0; gender < 3; gender++)
            {
                byte index = gender;
                Flags.Add(new(T("Dex9a_SeenGender" + gender), entry.GetIsGenderSeen(gender), value => { if (!_closed) _session.Dex.GetEntry(species).SetIsGenderSeen(index, value); }));
            }
            Flags.Add(new(T("Dex9a_Alpha"), entry.GetIsSeenAlpha(), value => { if (!_closed) _session.Dex.GetEntry(species).SetIsSeenAlpha(value); }));
            var megaNames = FormConverter.GetMegaFormNames(GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, GameInfo.Strings.Types);
            foreach (var flag in Pokedex9aCapabilities.GetMegaFlags(species, _session.Staged.SaveRevision))
                Flags.Add(new(MegaName(flag.Kind, megaNames), entry.GetIsSeenMega(flag.Index), value => { if (!_closed) _session.Dex.GetEntry(species).SetIsSeenMega(flag.Index, value); }));
            Languages.Clear();
            foreach (var language in Pokedex9aCapabilities.Languages)
                Languages.Add(new(T("Dex9a_Language" + (int)language), entry.GetLanguageFlag((int)language), value => { if (!_closed) _session.Dex.GetEntry(species).SetLanguageFlag((int)language, value); }));
            DisplayGenders.Clear();
            for (int gender = 0; gender < 4; gender++) DisplayGenders.Add(new(T("Dex9a_DisplayGender" + gender), gender));
            DisplayForm = entry.DisplayForm; DisplayGender = (byte)entry.DisplayGender;
            DisplayShiny = entry.GetDisplayIsShiny(); IsNew = entry.GetDisplayIsNew();
            AddUnknown(DisplayForms, DisplayForm); AddUnknown(DisplayGenders, DisplayGender);
            OnPropertyChanged(nameof(DisplayForm)); OnPropertyChanged(nameof(DisplayGender)); OnPropertyChanged(nameof(DisplayShiny)); OnPropertyChanged(nameof(IsNew));
            NotifyState();
        }
        finally { _loading = false; }
    }

    private static string MegaName(Dex9aMegaKind kind, MegaFormNames names) => kind switch
    {
        Dex9aMegaKind.X => names.X, Dex9aMegaKind.Y => names.Y, Dex9aMegaKind.Z => names.Z,
        Dex9aMegaKind.MeowsticMale => names.MeowsticM, Dex9aMegaKind.MeowsticFemale => names.MeowsticF,
        Dex9aMegaKind.MagearnaNormal => names.Magearna0, Dex9aMegaKind.MagearnaOriginal => names.Magearna1,
        Dex9aMegaKind.TatsugiriCurly => names.Tatsu0, Dex9aMegaKind.TatsugiriDroopy => names.Tatsu1,
        Dex9aMegaKind.TatsugiriStretchy => names.Tatsu2, _ => names.Regular,
    };
    private static void AddUnknown(ObservableCollection<ComboItem> choices, int value)
    { if (!choices.Any(item => item.Value == value)) choices.Add(new(LocalizedStrings.Instance.Format("Dex9a_UnknownValue", value), value)); }
    partial void OnDisplayFormChanged(int value) { if (!_loading && !_closed && CurrentSpecies != 0 && value is >= 0 and <= 255) _session.Dex.GetEntry(CurrentSpecies).DisplayForm = (byte)value; NotifyState(); }
    partial void OnDisplayGenderChanged(int value) { if (!_loading && !_closed && CurrentSpecies != 0 && value is >= 0 and <= 255) _session.Dex.GetEntry(CurrentSpecies).DisplayGender = (DisplayGender9a)value; NotifyState(); }
    partial void OnDisplayShinyChanged(bool value) { if (!_loading && !_closed && CurrentSpecies != 0) _session.Dex.GetEntry(CurrentSpecies).SetDisplayIsShiny(value); }
    partial void OnIsNewChanged(bool value) { if (!_loading && !_closed && CurrentSpecies != 0) _session.Dex.GetEntry(CurrentSpecies).SetDisplayIsNew(value); }

    [RelayCommand] private async Task BulkAsync(string actionName)
    {
        if (_closed || !IsSupported || CurrentSpecies == 0 || !Enum.TryParse<Dex9aBulkAction>(actionName, out var action) || !Enum.IsDefined(action)) return;
        ushort? species = WholeDex ? null : CurrentSpecies; bool shiny = IncludeShiny;
        string scope = species is null ? T("Dex9a_WholeDex") : SelectedSpecies!.Text;
        if (!await _dialogs.ShowConfirmationAsync(T("Dex9a_Action" + action), LocalizedStrings.Instance.Format("Dex9a_Confirm", scope), T("Dex9a_Apply"), T("Common_Cancel")) || _closed) return;
        _session.ApplyBulk(action, species, shiny); LoadEntry(); Error = string.Empty;
    }
    [RelayCommand] private void Reset() { if (_closed || !IsSupported) return; _session.Reset(); LoadEntry(); Error = string.Empty; }
    [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo() { _session.Undo(); LoadEntry(); }
    [RelayCommand(CanExecute = nameof(CanSave))] private void Save()
    {
        if (!CanSave) return;
        if (!_session.TryCommit()) { Error = T("Dex9a_Conflict"); return; }
        _closed = true; NotifyState(); CloseRequested?.Invoke(); Dispose();
    }
    [RelayCommand] private void Cancel() { if (_closed) return; _closed = true; NotifyState(); CloseRequested?.Invoke(); Dispose(); }
    private void NotifyState() { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanUndo)); SaveCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged(); }
    private void RefreshLanguage() { if (!_closed && IsSupported) { RebuildSpecies(); LoadEntry(); } }
    public void Dispose() { _closed = true; WeakReferenceMessenger.Default.UnregisterAll(this); NotifyState(); }
}

public partial class Dex9aBoolRow : ViewModelBase
{
    private readonly Action<bool> _set;
    public string Name { get; }
    [ObservableProperty] private bool _value;
    public Dex9aBoolRow(string name, bool value, Action<bool> set) { Name = name; _value = value; _set = set; }
    partial void OnValueChanged(bool value) => _set(value);
}

public partial class Dex9aFormRow : ViewModelBase
{
    private readonly Action<bool> _setCaught, _setSeen, _setShiny;
    public string Name { get; }
    public string CaughtAutomationName => LocalizedStrings.Instance.Format("Dex9a_FormStateLabel", Name, LocalizedStrings.Instance["Pokedex4Editor_Caught"]);
    public string SeenAutomationName => LocalizedStrings.Instance.Format("Dex9a_FormStateLabel", Name, LocalizedStrings.Instance["Pokedex4Editor_Seen"]);
    public string ShinyAutomationName => LocalizedStrings.Instance.Format("Dex9a_FormStateLabel", Name, LocalizedStrings.Instance["PokedexGen9Editor_Shiny"]);
    [ObservableProperty] private bool _caught;
    [ObservableProperty] private bool _seen;
    [ObservableProperty] private bool _shiny;
    public Dex9aFormRow(string name, bool caught, bool seen, bool shiny, Action<bool> setCaught, Action<bool> setSeen, Action<bool> setShiny)
    { Name = name; _caught = caught; _seen = seen; _shiny = shiny; _setCaught = setCaught; _setSeen = setSeen; _setShiny = setShiny; }
    partial void OnCaughtChanged(bool value) => _setCaught(value);
    partial void OnSeenChanged(bool value) => _setSeen(value);
    partial void OnShinyChanged(bool value) => _setShiny(value);
}
