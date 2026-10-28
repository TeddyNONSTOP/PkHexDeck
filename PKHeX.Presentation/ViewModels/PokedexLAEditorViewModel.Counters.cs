using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using System.Collections.ObjectModel;

namespace PKHeX.Presentation.ViewModels;

public partial class LASpeciesEntryViewModel
{
    public ObservableCollection<LAResearchCounterViewModel> AllCounters { get; } = [];
    private void LoadAllCounters()
    {
        AllCounters.Clear();
        var definitions = PokedexConstants8a.ResearchTasks[DexIndex - 1];
        foreach (var type in Enum.GetValues<PokedexResearchTaskType8a>().Where(type => type.CanSetCurrentValue()))
        {
            int count = type == PokedexResearchTaskType8a.UseMove ? 4 : type == PokedexResearchTaskType8a.DefeatWithMoveType ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                int index = count == 1 ? -1 : i;
                var definition = definitions.FirstOrDefault(task => task.Task == type && task.Index == index);
                int parameter = type switch
                {
                    PokedexResearchTaskType8a.UseMove => definition is null ? -1 : definition.Move,
                    PokedexResearchTaskType8a.DefeatWithMoveType => definition is null ? -1 : (int)definition.Type,
                    PokedexResearchTaskType8a.CatchAtTime => definition is null ? 0 : (int)definition.TimeOfDay,
                    _ => 0,
                };
                AllCounters.Add(new(_species, type, index, parameter, _dex, RefreshFromCounters));
            }
        }
    }
    private void RefreshFromTasks()
    {
        foreach (var counter in AllCounters) counter.RefreshValue();
        UpdateUnreportedLevel();
    }
    private void RefreshFromCounters()
    {
        foreach (var task in Tasks) task.RefreshValue();
        UpdateUnreportedLevel();
    }
}

public partial class LAResearchCounterViewModel : ViewModelBase
{
    private readonly ushort _species;
    private PokedexSave8a _dex;
    private readonly int _parameter;
    private readonly Action _changed;
    private readonly int _original;
    private bool _refreshing;
    public PokedexResearchTaskType8a Type { get; }
    public int Index { get; }
    [ObservableProperty] private int _currentValue;
    public int DisplayMinimum => Math.Min(0, _original);
    public int DisplayMaximum => Math.Max(PokedexConstants8a.MaxPokedexResearchPoints, _original);
    public bool IsValid => CurrentValue == _original || CurrentValue is >= 0 and <= PokedexConstants8a.MaxPokedexResearchPoints;
    public LAResearchCounterViewModel(ushort species, PokedexResearchTaskType8a type, int index, int parameter, PokedexSave8a dex, Action changed)
    {
        _species = species; Type = type; Index = index; _parameter = parameter; _dex = dex; _changed = changed;
        _dex.GetResearchTaskProgressByForce(species, type, index, out var value); _currentValue = _original = value;
    }
    public string Description
    {
        get
        {
            var labels = Util.GetStringList("tasks8a", GameInfo.CurrentLanguage);
            if (_parameter < 0 && Type is PokedexResearchTaskType8a.UseMove or PokedexResearchTaskType8a.DefeatWithMoveType)
                return string.Format(labels[(int)Type], LocalizedStrings.Instance.Format("DexLA_CounterSlot", Index + 1));
            return PokedexResearchTask8aExtensions.GetGenericTaskLabelString(Type, Index, _parameter, labels, Util.GetStringList("time_tasks8a", GameInfo.CurrentLanguage));
        }
    }
    partial void OnCurrentValueChanged(int value)
    {
        if (_refreshing) return;
        if (!IsValid) return;
        _dex.GetResearchTaskProgressByForce(_species, Type, Index, out int current);
        if (current != value) _dex.SetResearchTaskProgressByForce(_species, Type, value, Index);
        _changed();
    }
    internal void RefreshValue()
    {
        if (!IsValid) return;
        _dex.GetResearchTaskProgressByForce(_species, Type, Index, out var value);
        _refreshing = true;
        try { CurrentValue = value; } finally { _refreshing = false; }
    }
    internal void Rebind(PokedexSave8a dex) => _dex = dex;
    internal void RefreshLabels() => OnPropertyChanged(nameof(Description));
}
