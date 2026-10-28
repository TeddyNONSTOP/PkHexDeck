using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

public partial class EventFlagsEditorViewModel : EventEditorViewModel
{
    private readonly SaveFile _sav;
    private readonly IEventFlagArray? _flagArray;

    public EventFlagsEditorViewModel(SaveFile sav)
    {
        _sav = sav;
        _flagArray = GetFlagArray(sav);

        if (_flagArray is not null)
        {
            FlagCount = _flagArray.EventFlagCount;
            IsSupported = true;
            LoadFlags();
        }
        else
        {
            FlagCount = 0;
            IsSupported = false;
        }
    }

    private static IEventFlagArray? GetFlagArray(SaveFile sav)
    {
        // Try to get event flags from various save types
        if (sav is IEventFlagProvider37 provider)
            return provider.EventWork;
        if (sav is IEventFlagArray flagArray)
            return flagArray;
        return null;
    }

    public int FlagCount { get; }
    public int MaxFlagIndex => Math.Max(0, FlagCount - 1);
    public override bool IsSupported { get; }

    [ObservableProperty]
    private ObservableCollection<EventFlagViewModel> _flags = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EventFlagViewModel> _filteredFlags = [];

    [ObservableProperty]
    private int _selectedFlagIndex;

    [ObservableProperty]
    private bool _selectedFlagValue;

    partial void OnSearchTextChanged(string value)
    {
        FilterFlags();
    }

    partial void OnSelectedFlagIndexChanged(int value)
    {
        if (FlagCount == 0) return;
        if (value < 0 || value >= FlagCount)
        {
            SelectedFlagIndex = Math.Clamp(value, 0, MaxFlagIndex);
            return;
        }
        SelectedFlagValue = Flags[value].IsSet;
    }

    partial void OnSelectedFlagValueChanged(bool value)
    {
        if (_flagArray is not null && SelectedFlagIndex >= 0 && SelectedFlagIndex < FlagCount)
        {
            Flags[SelectedFlagIndex].IsSet = value;
        }
    }

    private void LoadFlags()
    {
        if (_flagArray is null) return;

        foreach (var old in Flags)
            old.PropertyChanged -= FlagChanged;
        Flags.Clear();
        for (int i = 0; i < FlagCount; i++)
        {
            var isSet = _flagArray.GetEventFlag(i);
            var flag = new EventFlagViewModel(i, isSet);
            flag.PropertyChanged += FlagChanged;
            Flags.Add(flag);
        }
        if (FlagCount != 0)
            SelectedFlagValue = Flags[SelectedFlagIndex].IsSet;
        FilterFlags();
    }

    private void FlagChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is EventFlagViewModel row && row.Index == SelectedFlagIndex
            && e.PropertyName == nameof(EventFlagViewModel.IsSet))
            SelectedFlagValue = row.IsSet;
    }

    private void FilterFlags()
    {
        FilteredFlags.Clear();
        var search = SearchText.ToLowerInvariant();

        foreach (var flag in Flags)
        {
            if (string.IsNullOrEmpty(search) ||
                flag.Index.ToString().Contains(search) ||
                flag.HexIndex.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFlags.Add(flag);
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_flagArray is null) return;

        foreach (var flag in Flags)
        {
            if (_flagArray.GetEventFlag(flag.Index) == flag.IsSet) continue;
            _flagArray.SetEventFlag(flag.Index, flag.IsSet);
            _sav.State.Edited = true;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        LoadFlags();
    }

    [RelayCommand]
    private void SetAll()
    {
        foreach (var flag in Flags)
        {
            flag.IsSet = true;
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var flag in Flags)
        {
            flag.IsSet = false;
        }
    }
}

public partial class EventFlagViewModel : ViewModelBase
{
    public EventFlagViewModel(int index, bool isSet)
    {
        Index = index;
        _isSet = isSet;
        HexIndex = $"0x{index:X4}";
    }

    public int Index { get; }
    public string HexIndex { get; }

    [ObservableProperty]
    private bool _isSet;
}
