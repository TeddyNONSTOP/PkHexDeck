using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Application.Services;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class FestivalPlazaEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly FestivalPlazaDataSession? _session;
    private bool _loading;
    private bool _closed;
    private readonly IDialogService? _dialogs;
    public Action? CloseRequested { get; set; }
    private JoinFesta7? _festa;

    public FestivalPlazaEditorViewModel(SaveFile sav, IDialogService? dialogs = null)
    {
        _dialogs = dialogs;
        if (sav is SAV7 sav7)
        {
            _session = new FestivalPlazaDataSession(sav7);
            _festa = _session.WorkingSave.Festa;
            IsSupported = true;
            LoadData();
        }
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((FestivalPlazaEditorViewModel)recipient).RefreshLanguage());
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
    private void RefreshLanguage()
    {
        foreach (var row in Messages) row.RefreshLanguage();
        foreach (var row in Rewards) row.RefreshLanguage();
        foreach (var row in Phrases) row.RefreshLanguage();
        foreach (var row in Facilities) row.RefreshLanguage();
        if (TimestampError.Length != 0) TimestampError = LocalizedStrings.Instance["PlazaSession_TimestampError"];
        ValidationChanged();
    }
    public bool IsSupported { get; }

    [ObservableProperty]
    private string _plazaName = string.Empty;

    partial void OnPlazaNameChanged(string value)
    {
        if (!_loading && !_closed && _festa is not null)
            _festa.FestivalPlazaName = value;
    }

    [ObservableProperty]
    private int _currentFC;

    partial void OnCurrentFCChanged(int value)
    {
        if (!_loading && !_closed && _festa is not null && value is >= 0 and <= 9999999)
            _festa.FestaCoins = value;
        OnPropertyChanged(nameof(TotalFC));
        if (!_loading) ValidationChanged();
    }

    [ObservableProperty]
    private int _usedFC;

    partial void OnUsedFCChanged(int value)
    {
        if (!_loading && !_closed && _session is not null && value >= 0 && value <= _session.WorkingSave.GetRecordMax(38))
            _session.WorkingSave.SetRecord(38, value);
        OnPropertyChanged(nameof(TotalFC));
        if (!_loading) ValidationChanged();
    }

    public long TotalFC => (long)CurrentFC + UsedFC;

    [ObservableProperty]
    private int _rank;

    partial void OnRankChanged(int value)
    {
        if (!_loading && !_closed && _festa is not null && value is >= 0 and <= ushort.MaxValue)
            _festa.FestaRank = (ushort)value;
        OnPropertyChanged(nameof(RankFCRange));
        if (!_loading) ValidationChanged();
    }

    public string RankFCRange => GetRankText(Rank);

    [ObservableProperty]
    private ObservableCollection<FacilityViewModel> _facilities = [];

    [ObservableProperty]
    private FacilityViewModel? _selectedFacility;

    private void LoadData()
    {
        if (_festa is null) return;

        _loading = true;
        PlazaName = _festa.FestivalPlazaName;
        CurrentFC = _festa.FestaCoins;
        UsedFC = _session!.WorkingSave.GetRecord(38);
        Rank = _festa.FestaRank;

        // Load facilities
        Facilities.Clear();
        for (int i = 0; i < JoinFesta7.FestaFacilityCount; i++)
        {
            var facility = _festa.GetFestaFacility(i);
            Facilities.Add(new FacilityViewModel(i, facility, _session.WorkingSave is SAV7USUM, ValidationChanged));
        }

        if (Facilities.Count > 0)
            SelectedFacility = Facilities[0];
        LoadAdditionalFields();
        _loading = false;
        ValidationChanged();
    }

    private static string GetRankText(int rank)
    {
        if (rank < 1) return string.Empty;
        if (rank == 1) return "0 - 5";
        if (rank == 2) return "6 - 15";
        if (rank == 3) return "16 - 30";
        if (rank <= 10)
        {
            int i = ((rank - 1) * (rank - 2) * 5) + 1;
            return $"{i} - {i + ((rank - 1) * 10) - 1}";
        }
        if (rank <= 20)
        {
            int i = (rank * 100) - 649;
            return $"{i} - {i + 99}";
        }
        if (rank <= 70)
        {
            int j = (rank - 1) / 10;
            int i = (rank * ((j * 30) + 60)) - ((j * j * 150) + (j * 180) + 109);
            return $"{i} - {i + (j * 30) + 59}";
        }
        if (rank <= 100)
        {
            int i = (rank * 270) - 8719;
            return $"{i} - {i + 269}";
        }
        if (rank <= 998)
        {
            int i = (rank * 300) - 11749;
            return $"{i} - {i + 299}";
        }
        if (rank == 999)
            return "287951+";
        return string.Empty;
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_closed || _session is null) return;
        _session.Reset();
        _festa = _session.WorkingSave.Festa;
        LoadData();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave || _session is null || !_session.TryCommit(out _)) return;
        _closed = true;
        ValidationChanged();
        CloseRequested?.Invoke();
        Dispose();
    }

    [RelayCommand]
    private void Cancel()
    {
        _closed = true;
        ValidationChanged();
        CloseRequested?.Invoke();
        Dispose();
    }
}
