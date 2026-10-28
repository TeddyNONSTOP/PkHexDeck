using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using CommunityToolkit.Mvvm.Messaging;

namespace PKHeX.Presentation.ViewModels;

public partial class PokedexLAEditorViewModel : ViewModelBase, IDisposable
{
    private SAV8LA _sav;
    private PokedexSave8a _dex;
    private readonly TrainerScBlockDataSession<SAV8LA> _session;
    private IReadOnlyList<LASpeciesEntryViewModel> _entries = [];
    private bool _closed;
    private int _epoch;
    private readonly IDialogService? _dialogs;
    [ObservableProperty] private bool _entirePokedex;
    public bool CanUndo => !_closed && _session.CanUndo;
    public bool CanReport => _entries.All(entry => entry.Tasks.All(task => task.IsAvailable));
    [ObservableProperty] private string _error = string.Empty;
    public bool HasError => Error.Length != 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    public PokedexLAEditorViewModel(SaveFile sav, IDialogService? dialogs = null)
    {
        _session = new((SAV8LA)sav);
        _dialogs = dialogs;
        _sav = _session.Staged;
        _dex = _sav.Blocks.PokedexSave;
        
        LoadSpecies();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((PokedexLAEditorViewModel)recipient).RefreshLanguage());
    }

    [ObservableProperty]
    private ObservableCollection<LASpeciesEntryViewModel> _speciesList = [];

    [ObservableProperty]
    private LASpeciesEntryViewModel? _selectedSpecies;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void LoadSpecies()
    {
        var speciesNames = GameInfo.Strings.Species;
        var list = new List<LASpeciesEntryViewModel>();
        for (ushort s = 1; s <= _sav.Personal.MaxSpeciesID; s++)
        {
            var hisuiDex = PokedexSave8a.GetDexIndex(PokedexType8a.Hisui, s);
            if (hisuiDex == 0) continue;

            ushort species = s;
            list.Add(new LASpeciesEntryViewModel(s, hisuiDex, speciesNames[s], _dex, _sav, () => ReportCurrentAsync(species)));
        }

        _entries = list.OrderBy(z => z.DexIndex).ToArray();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = SelectedSpecies;
        string query = SearchText.Trim();
        SpeciesList = new(_entries.Where(entry => query.Length == 0 || entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.DexIndex.ToString().Contains(query, StringComparison.Ordinal)));
        SelectedSpecies = selected is not null && SpeciesList.Contains(selected) ? selected : SpeciesList.FirstOrDefault();
    }

    [RelayCommand]
    private void Save()
    {
        if (_closed) return;
        if (_entries.Any(entry => !entry.IsValid)) { Error = LocalizedStrings.Instance["Trainer7_InvalidValues"]; return; }
        _epoch++;
        if (!_session.TryCommit(_ => { foreach (var entry in _entries) entry.Save(); }))
        {
            _sav = _session.Staged; _dex = _sav.Blocks.PokedexSave;
            foreach (var entry in _entries) entry.Rebind(_dex, _sav);
            Error = LocalizedStrings.Instance["LgpeTrainer_Conflict"]; return;
        }
        Reload();
    }

    [RelayCommand]
    private Task ReportAll() => RunScopedAsync(LocalizedStrings.Instance["PokedexLAEditor_ReportAll"], true, null, () => _dex.UpdateAllReportPoke(), requiresReportingData: true);
    private Task ReportCurrentAsync(ushort species) => RunScopedAsync(LocalizedStrings.Instance["PokedexLAEditor_ReportCurrentSpecies"], false, species, () => _dex.UpdateSpecificReportPoke(species), requiresReportingData: true);
    [RelayCommand] private Task CompleteTasks() => EditTasksAsync(true);
    [RelayCommand] private Task ClearTasks() => EditTasksAsync(false);
    private Task EditTasksAsync(bool complete)
    {
        bool whole = EntirePokedex; ushort? species = whole ? null : SelectedSpecies?.Species;
        if (!whole && species is null) return Task.CompletedTask;
        return RunScopedAsync(LocalizedStrings.Instance[complete ? "DexLA_CompleteTasks" : "DexLA_ClearTasks"], whole, species, () =>
        {
            foreach (var entry in _entries.Where(entry => whole || entry.Species == species))
            {
                if (complete) foreach (var task in entry.Tasks.Where(task => task.CanEdit)) task.CurrentValue = task.Thresholds.LastOrDefault();
                else foreach (var counter in entry.AllCounters) counter.CurrentValue = 0;
            }
        }, () => EntirePokedex == whole);
    }
    private async Task RunScopedAsync(string title, bool whole, ushort? species, Action action, Func<bool>? validScope = null, bool requiresReportingData = false)
    {
        if (_closed || _dialogs is null || requiresReportingData && !CanReport || _entries.Any(entry => !entry.IsValid)) return;
        int epoch = _epoch;
        string scope = whole ? LocalizedStrings.Instance["Dex9a_WholeDex"] : _entries.Single(entry => entry.Species == species).DisplayName;
        if (!await _dialogs.ShowConfirmationAsync(title, LocalizedStrings.Instance.Format("Dex9a_Confirm", scope), LocalizedStrings.Instance["Dex9a_Apply"], LocalizedStrings.Instance["Common_Cancel"])) return;
        if (_closed || _epoch != epoch || (!whole && SelectedSpecies?.Species != species) || validScope?.Invoke() is false || _entries.Any(entry => !entry.IsValid)) return;
        foreach (var entry in _entries) entry.Save();
        _session.ApplyAction(_ => action()); _epoch++; Reload();
    }

    private void Reload()
    {
        ushort? selected = SelectedSpecies?.Species;
        _sav = _session.Staged; _dex = _sav.Blocks.PokedexSave;
        LoadSpecies(); Error = string.Empty;
        SelectedSpecies = SpeciesList.FirstOrDefault(entry => entry.Species == selected) ?? SelectedSpecies;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanReport));
    }
    [RelayCommand] private void Reset() { if (_closed) return; _epoch++; _session.Reset(); Reload(); }
    [RelayCommand] private void Undo() { if (_closed) return; _epoch++; _session.Undo(); Reload(); }
    public void RefreshLanguage()
    {
        if (_closed) return;
        foreach (var entry in _entries) entry.RefreshLabels();
        ApplyFilter();
    }
    public void Dispose() { _closed = true; _epoch++; WeakReferenceMessenger.Default.UnregisterAll(this); }
}

public partial class LASpeciesEntryViewModel : ViewModelBase
{
    private readonly ushort _species;
    private PokedexSave8a _dex;
    private SAV8LA _sav;
    private readonly Func<Task>? _report;
    public ushort Species => _species;
    internal void Rebind(PokedexSave8a dex, SAV8LA save)
    {
        _dex = dex; _sav = save;
        foreach (var form in Forms) form.Rebind(dex);
        foreach (var task in Tasks) task.Rebind(dex);
        foreach (var counter in AllCounters) counter.Rebind(dex);
    }

    public LASpeciesEntryViewModel(ushort species, int dexIndex, string name, PokedexSave8a dex, SAV8LA sav, Func<Task>? report = null)
    {
        _species = species;
        DexIndex = dexIndex;
        Name = name;
        _dex = dex;
        _sav = sav;
        _report = report;

        LoadForms();
        Load();
    }

    public int DexIndex { get; }
    [ObservableProperty] private string _name = string.Empty;
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    public string DisplayName => $"{DexIndex:000} - {Name}";
    [ObservableProperty] private int _displayForm;
    [ObservableProperty] private bool _displayAlpha;
    [ObservableProperty] private bool _displayShiny;
    [ObservableProperty] private bool _displayFemale;
    private (int Form, bool Alpha, bool Shiny, bool Female) _originalDisplay;
    public IReadOnlyList<ComboItem> DisplayForms
    {
        get
        {
            var choices = Forms.Select(form => new ComboItem(form.Name, form.Form)).ToList();
            if (choices.All(item => item.Value != DisplayForm)) choices.Add(new(LocalizedStrings.Instance.Format("RaidSession_UnknownType", DisplayForm), DisplayForm));
            return choices;
        }
    }
    public void RefreshLabels()
    {
        Name = GameInfo.Strings.Species[_species];
        var names = FormConverter.GetFormList(_species, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen8a);
        foreach (var form in Forms)
        {
            form.Name = form.Form < names.Length && names[form.Form].Length != 0 ? names[form.Form] : LocalizedStrings.Instance["Dex9a_BaseForm"];
            form.RefreshCulture();
        }
        int display = DisplayForm;
        OnPropertyChanged(nameof(DisplayForms)); DisplayForm = display;
        foreach (var task in Tasks) task.RefreshLabels();
        foreach (var counter in AllCounters) counter.RefreshLabels();
    }
    public bool IsValid => Tasks.All(task => task.IsValid) && AllCounters.All(counter => counter.IsValid) && Forms.All(form => form.IsValid) &&
        (DisplayForm == _originalDisplay.Form || Forms.Any(form => form.Form == DisplayForm));

    [ObservableProperty]
    private ObservableCollection<LAFormEntryViewModel> _forms = [];

    [ObservableProperty]
    private LAFormEntryViewModel? _selectedForm;

    [ObservableProperty]
    private ObservableCollection<LAResearchTaskViewModel> _tasks = [];

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _isPerfect;

    [ObservableProperty]
    private bool _isSolitudeComplete;

    [ObservableProperty]
    private int _reportedResearchLevel;

    [ObservableProperty]
    private int _unreportedResearchLevel;

    private void LoadForms()
    {
        Forms.Clear();
        var personal = _sav.Personal[_species];
        var formCount = personal.FormCount;
        var formNames = FormConverter.GetFormList(_species, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen8a);

        for (byte f = 0; f < formCount; f++)
        {
            if (!_dex.HasFormStorage(_species, f) || _dex.IsBlacklisted(_species, f))
                continue;

            string name = f < formNames.Length ? formNames[f] : string.Empty;
            Forms.Add(new LAFormEntryViewModel(_species, f, name.Length == 0 ? LocalizedStrings.Instance["Dex9a_BaseForm"] : name, _dex, RefreshFromCounters));
        }
        
        if (Forms.Count > 0)
            SelectedForm = Forms[0];
    }

    public void Load()
    {
        DisplayForm = _dex.GetSelectedForm(_species); DisplayAlpha = _dex.GetSelectedAlpha(_species);
        DisplayShiny = _dex.GetSelectedShiny(_species); DisplayFemale = _dex.GetSelectedGender1(_species);
        _originalDisplay = (DisplayForm, DisplayAlpha, DisplayShiny, DisplayFemale);
        IsComplete = _dex.IsComplete(_species);
        IsSolitudeComplete = _dex.GetSolitudeComplete(_species);
        
        ReportedResearchLevel = _dex.GetPokeResearchRate(_species);
        
        // Load Tasks
        Tasks.Clear();
        if (PokedexConstants8a.ResearchTasks.Length > DexIndex - 1)
        {
            var tasks = PokedexConstants8a.ResearchTasks[DexIndex - 1];
            for (int i = 0; i < tasks.Length; i++)
            {
                Tasks.Add(new LAResearchTaskViewModel(_species, i, tasks[i], _dex, RefreshFromTasks));
            }
        }

        foreach (var form in Forms)
        {
            form.Load();
        }

        IsPerfect = Tasks.All(task => task.IsAvailable) && _dex.IsPerfect(_species);

        LoadAllCounters(); UpdateUnreportedLevel();
    }

    private void UpdateUnreportedLevel()
    {
        int unreported = ReportedResearchLevel;
        foreach (var task in Tasks.Where(task => task.IsAvailable))
        {
            int unreportedLevels = _dex.GetResearchTaskLevel(_species, task.Index, out _, out _, out _);
            unreported += unreportedLevels * task.PointsPerLevel;
        }
        UnreportedResearchLevel = unreported;
    }

    public void Save()
    {
        if (!IsValid) return;
        _dex.SetSolitudeComplete(_species, IsSolitudeComplete);
        foreach (var form in Forms)
        {
            form.Save();
        }
        foreach (var task in Tasks)
        {
            task.Save();
        }
        if ((DisplayForm, DisplayAlpha, DisplayShiny, DisplayFemale) != _originalDisplay)
            _dex.SetSelectedGenderForm(_species, (byte)DisplayForm, DisplayFemale, DisplayShiny, DisplayAlpha);
    }

    [RelayCommand]
    private Task ReportSpecies() => _report?.Invoke() ?? Task.CompletedTask;
}

public partial class LAFormEntryViewModel : ViewModelBase
{
    private readonly ushort _species;
    private readonly byte _form;
    private PokedexSave8a _dex;
    internal void Rebind(PokedexSave8a dex) => _dex = dex;
    private readonly Action? _changed;
    private bool _loading;

    public LAFormEntryViewModel(ushort species, byte form, string name, PokedexSave8a dex, Action? changed = null)
    {
        _species = species;
        _form = form;
        Name = string.IsNullOrEmpty(name) ? "Base" : name;
        _dex = dex;
        _changed = changed;
    }

    [ObservableProperty] private string _name = string.Empty;
    public int Form => _form;
    [ObservableProperty] private bool _hasMaximum;
    [ObservableProperty] private string _minimumHeight = string.Empty;
    [ObservableProperty] private string _maximumHeight = string.Empty;
    [ObservableProperty] private string _minimumWeight = string.Empty;
    [ObservableProperty] private string _maximumWeight = string.Empty;
    private (bool Both, float MinHeight, float MaxHeight, float MinWeight, float MaxWeight) _originalSize;
    private (string MinHeight, string MaxHeight, string MinWeight, string MaxWeight) _originalSizeText;
    private bool SizeUnchanged => HasMaximum == _originalSize.Both && (MinimumHeight, MaximumHeight, MinimumWeight, MaximumWeight) == _originalSizeText;
    private System.Globalization.CultureInfo _sizeCulture = System.Globalization.CultureInfo.CurrentCulture;
    private bool ParseSize(string text, out float value) => float.TryParse(text, System.Globalization.NumberStyles.Float, _sizeCulture, out value) && float.IsFinite(value) && value >= 0;
    public void RefreshCulture()
    {
        var next = System.Globalization.CultureInfo.CurrentCulture;
        string Convert(string text) => float.TryParse(text, System.Globalization.NumberStyles.Float, _sizeCulture, out var value) ? value.ToString("R", next) : text;
        MinimumHeight = Convert(MinimumHeight); MaximumHeight = Convert(MaximumHeight);
        MinimumWeight = Convert(MinimumWeight); MaximumWeight = Convert(MaximumWeight);
        _originalSizeText = (_originalSize.MinHeight.ToString("R", next), _originalSize.MaxHeight.ToString("R", next), _originalSize.MinWeight.ToString("R", next), _originalSize.MaxWeight.ToString("R", next));
        _sizeCulture = next;
    }
    public bool IsValid => SizeUnchanged || ParseSize(MinimumHeight, out var minHeight) && ParseSize(MaximumHeight, out var maxHeight) &&
        ParseSize(MinimumWeight, out var minWeight) && ParseSize(MaximumWeight, out var maxWeight) && (!HasMaximum || minHeight <= maxHeight && minWeight <= maxWeight);

    [ObservableProperty] private bool _seen0;
    [ObservableProperty] private bool _seen1;
    [ObservableProperty] private bool _seen2;
    [ObservableProperty] private bool _seen3;
    [ObservableProperty] private bool _seen4;
    [ObservableProperty] private bool _seen5;
    [ObservableProperty] private bool _seen6;
    [ObservableProperty] private bool _seen7;

    [ObservableProperty] private bool _obtained0;
    [ObservableProperty] private bool _obtained1;
    [ObservableProperty] private bool _obtained2;
    [ObservableProperty] private bool _obtained3;
    [ObservableProperty] private bool _obtained4;
    [ObservableProperty] private bool _obtained5;
    [ObservableProperty] private bool _obtained6;
    [ObservableProperty] private bool _obtained7;

    [ObservableProperty] private bool _caught0;
    [ObservableProperty] private bool _caught1;
    [ObservableProperty] private bool _caught2;
    [ObservableProperty] private bool _caught3;
    [ObservableProperty] private bool _caught4;
    [ObservableProperty] private bool _caught5;
    [ObservableProperty] private bool _caught6;
    [ObservableProperty] private bool _caught7;

    public void Load()
    {
        _loading = true;
        _dex.GetSizeStatistics(_species, _form, out var both, out var minHeight, out var maxHeight, out var minWeight, out var maxWeight);
        _originalSize = (both, minHeight, maxHeight, minWeight, maxWeight);
        HasMaximum = both;
        MinimumHeight = minHeight.ToString("R", System.Globalization.CultureInfo.CurrentCulture); MaximumHeight = maxHeight.ToString("R", System.Globalization.CultureInfo.CurrentCulture);
        MinimumWeight = minWeight.ToString("R", System.Globalization.CultureInfo.CurrentCulture); MaximumWeight = maxWeight.ToString("R", System.Globalization.CultureInfo.CurrentCulture);
        _originalSizeText = (MinimumHeight, MaximumHeight, MinimumWeight, MaximumWeight);
        var seen = _dex.GetPokeSeenInWildFlags(_species, _form);
        var obtained = _dex.GetPokeObtainFlags(_species, _form);
        var caught = _dex.GetPokeCaughtInWildFlags(_species, _form);

        Seen0 = (seen & 1) != 0; Seen1 = (seen & 2) != 0; Seen2 = (seen & 4) != 0; Seen3 = (seen & 8) != 0;
        Seen4 = (seen & 16) != 0; Seen5 = (seen & 32) != 0; Seen6 = (seen & 64) != 0; Seen7 = (seen & 128) != 0;

        Obtained0 = (obtained & 1) != 0; Obtained1 = (obtained & 2) != 0; Obtained2 = (obtained & 4) != 0; Obtained3 = (obtained & 8) != 0;
        Obtained4 = (obtained & 16) != 0; Obtained5 = (obtained & 32) != 0; Obtained6 = (obtained & 64) != 0; Obtained7 = (obtained & 128) != 0;

        Caught0 = (caught & 1) != 0; Caught1 = (caught & 2) != 0; Caught2 = (caught & 4) != 0; Caught3 = (caught & 8) != 0;
        Caught4 = (caught & 16) != 0; Caught5 = (caught & 32) != 0; Caught6 = (caught & 64) != 0; Caught7 = (caught & 128) != 0;
        _loading = false;
    }
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (!_loading && args.PropertyName is { } name && (name.StartsWith("Seen", StringComparison.Ordinal) || name.StartsWith("Obtained", StringComparison.Ordinal) || name.StartsWith("Caught", StringComparison.Ordinal)))
        { Save(); _changed?.Invoke(); }
    }

    public void Save()
    {
        if (!IsValid) return;
        if (!SizeUnchanged)
        {
            ParseSize(MinimumHeight, out var minHeight); ParseSize(MaximumHeight, out var maxHeight);
            ParseSize(MinimumWeight, out var minWeight); ParseSize(MaximumWeight, out var maxWeight);
            if ((HasMaximum, minHeight, maxHeight, minWeight, maxWeight) != _originalSize)
                _dex.SetSizeStatistics(_species, _form, HasMaximum, minHeight, maxHeight, minWeight, maxWeight);
        }
        byte seen = 0;
        if (Seen0) seen |= 1; if (Seen1) seen |= 2; if (Seen2) seen |= 4; if (Seen3) seen |= 8;
        if (Seen4) seen |= 16; if (Seen5) seen |= 32; if (Seen6) seen |= 64; if (Seen7) seen |= 128;
        _dex.SetPokeSeenInWildFlags(_species, _form, seen);

        byte obtained = 0;
        if (Obtained0) obtained |= 1; if (Obtained1) obtained |= 2; if (Obtained2) obtained |= 4; if (Obtained3) obtained |= 8;
        if (Obtained4) obtained |= 16; if (Obtained5) obtained |= 32; if (Obtained6) obtained |= 64; if (Obtained7) obtained |= 128;
        _dex.SetPokeObtainFlags(_species, _form, obtained);

        byte caught = 0;
        if (Caught0) caught |= 1; if (Caught1) caught |= 2; if (Caught2) caught |= 4; if (Caught3) caught |= 8;
        if (Caught4) caught |= 16; if (Caught5) caught |= 32; if (Caught6) caught |= 64; if (Caught7) caught |= 128;
        _dex.SetPokeCaughtInWildFlags(_species, _form, caught);
    }
}

public partial class LAResearchTaskViewModel : ViewModelBase
{
    private readonly ushort _species;
    private readonly PokedexResearchTask8a _task;
    private PokedexSave8a _dex;
    internal void Rebind(PokedexSave8a dex) => _dex = dex;
    private readonly Action? _changed;
    private int _originalValue;
    private bool _refreshing;

    public LAResearchTaskViewModel(ushort species, int taskIndex, PokedexResearchTask8a task, PokedexSave8a dex, Action? changed = null)
    {
        _species = species;
        Index = taskIndex;
        _task = task;
        _dex = dex;
        _changed = changed;

        try
        {
            _dex.GetResearchTaskLevel(species, taskIndex, out _, out var value, out _);
            _currentValue = _originalValue = value;
        }
        catch (ArgumentOutOfRangeException error) when (error.ParamName == "type" && !task.Task.CanSetCurrentValue())
        { IsAvailable = false; }
    }

    public int Index { get; }
    public string Description => _task.GetTaskLabelString(Util.GetStringList("tasks8a", GameInfo.CurrentLanguage),
        Util.GetStringList("time_tasks8a", GameInfo.CurrentLanguage), Util.GetStringList("species_tasks8a", GameInfo.CurrentLanguage));
    public bool CanEdit => _task.Task.CanSetCurrentValue();
    public bool IsAvailable { get; } = true;
    public bool HasBonus => _task.PointsBonus != 0;
    public string BonusText => LocalizedStrings.Instance.Format("DexLA_Bonus", _task.PointsBonus);
    public bool IsRequired => _task.RequiredForCompletion;
    public IReadOnlyList<byte> Thresholds => _task.TaskThresholds;
    public int AchievedThresholds => _task.TaskThresholds.Count(threshold => CurrentValue >= threshold);
    public int ReportedThresholds { get { _dex.GetResearchTaskLevel(_species, Index, out var reported, out _, out _); return Math.Max(0, reported - 1); } }
    public bool IsValid => !CanEdit || CurrentValue == _originalValue || CurrentValue is >= 0 and <= PokedexConstants8a.MaxPokedexResearchPoints;
    public int DisplayMinimum => Math.Min(0, _originalValue);
    public int DisplayMaximum => Math.Max(PokedexConstants8a.MaxPokedexResearchPoints, _originalValue);
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(ThresholdProgress)); OnPropertyChanged(nameof(BonusText));
    }
    internal void RefreshValue()
    {
        if (!IsValid || !IsAvailable) return;
        _dex.GetResearchTaskLevel(_species, Index, out _, out var value, out _);
        _refreshing = true;
        try { CurrentValue = value; } finally { _refreshing = false; }
        OnPropertyChanged(nameof(ThresholdProgress));
    }
    public string ThresholdProgress => !IsAvailable ? LocalizedStrings.Instance["DexLA_Unavailable"] : LocalizedStrings.Instance.Format("DexLA_ThresholdProgress", string.Join(" / ", Thresholds), AchievedThresholds, Thresholds.Count, ReportedThresholds);
    public int PointsPerLevel => _task.PointsSingle + _task.PointsBonus;

    [ObservableProperty]
    private int _currentValue;

    public void Save()
    {
        if (!CanEdit || !IsValid) return;
        _dex.GetResearchTaskProgressByForce(_species, _task.Task, _task.Index, out var current);
        if (CurrentValue != current) _dex.SetResearchTaskProgressByForce(_species, _task, CurrentValue);
    }
    partial void OnCurrentValueChanged(int value)
    {
        if (_refreshing) return;
        Save(); _changed?.Invoke();
        OnPropertyChanged(nameof(ThresholdProgress));
    }
}
