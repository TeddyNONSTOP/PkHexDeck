using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Raid9EditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SvRaidDataSession? _session;
    private readonly IDialogService? _dialogs;
    private bool _closed;
    public Action? CloseRequested { get; set; }
    public IReadOnlyList<Raid9RegionViewModel> Regions { get; private set; } = [];
    public IReadOnlyList<RaidSevenStarItem> RaidItems { get; private set; } = [];
    public bool IsSupported => _session is not null && (Regions.Count != 0 || RaidItems.Count != 0);
    public bool CanSave => !_closed && IsSupported && Regions.All(r => r.SeedError.Length == 0 && r.Raids.All(row => row.SeedError.Length == 0));
    public bool IsRaidTab => SelectedTab == 0;
    public bool CanCopy => IsRaidTab && CanSave && SelectedRaid is not null && _dialogs is not null;
    public string Region => SelectedRegion?.Name ?? string.Empty;
    public string Title => SelectedTab == 1 ? LocalizedStrings.Instance["RaidSevenStar9Editor_Title"]
        : $"{LocalizedStrings.Instance["Raid9Editor_Title"]} ({Region})";
    public bool HasStatus => StatusText.Length != 0;
    public bool HasValidationError => ValidationSummary.Length != 0;
    public string ValidationSummary
    {
        get
        {
            var invalid = Regions.FirstOrDefault(r => r.SeedError.Length != 0 || r.Raids.Any(row => row.SeedError.Length != 0));
            return invalid is null ? string.Empty : LocalizedStrings.Instance.Format("TeraSession_ValidationSummary", invalid.Name);
        }
    }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title), nameof(IsRaidTab))] [NotifyCanExecuteChangedFor(nameof(CopyToOthersCommand))] private int _selectedTab;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasVisibleRaids))] private IReadOnlyList<Raid9ViewModel> _raids9 = [];
    public bool HasVisibleRaids => Raids9.Count != 0;
    [ObservableProperty] private Raid9ViewModel? _selectedRaid;
    [ObservableProperty] private RaidSevenStarItem? _selectedRecord;
    [ObservableProperty] private bool _includeSeed;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusText = string.Empty;
    private Raid9RegionViewModel? _selectedRegion;
    public Raid9RegionViewModel? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (ReferenceEquals(value, _selectedRegion)) return;
            var preferred = value?.SelectedRaid;
            SetProperty(ref _selectedRegion, value);
            OnPropertyChanged(nameof(Region)); OnPropertyChanged(nameof(Title));
            Filter(preferred);
        }
    }

    public Raid9EditorViewModel(SaveFile sav, string region = "Paldea", IDialogService? dialogs = null, int initialTab = 0)
    {
        _dialogs = dialogs;
        _selectedTab = initialTab;
        if (sav is SAV9SV sv) _session = new SvRaidDataSession(sv);
        ReloadRows();
        SelectedRegion = Regions.FirstOrDefault(r => r.Origin == (region switch
        {
            "Kitakami" => TeraRaidOrigin.Kitakami,
            "Blueberry" => TeraRaidOrigin.BlueberryAcademy,
            _ => TeraRaidOrigin.Paldea,
        })) ?? Regions.FirstOrDefault();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((Raid9EditorViewModel)recipient).RefreshLanguage());
    }

    partial void OnSearchTextChanged(string value) => Filter(SelectedRaid);
    partial void OnSelectedRaidChanged(Raid9ViewModel? value)
    {
        if (SelectedRegion is not null) SelectedRegion.SelectedRaid = value;
        CopyToOthersCommand.NotifyCanExecuteChanged();
    }
    private void Filter(Raid9ViewModel? preferred)
    {
        var search = SearchText.Trim();
        Raids9 = SelectedRegion?.Raids.Where(row => search.Length == 0 || row.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToArray() ?? [];
        SelectedRaid = preferred is not null && Raids9.Contains(preferred) ? preferred : Raids9.FirstOrDefault();
    }
    private void ValidationChanged()
    {
        OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(ValidationSummary)); OnPropertyChanged(nameof(HasValidationError));
        SaveCommand.NotifyCanExecuteChanged(); CopyToOthersCommand.NotifyCanExecuteChanged();
    }
    private void ReloadRows()
    {
        if (_session is null) return;
        DetachRows();
        var origin = SelectedRegion?.Origin ?? TeraRaidOrigin.Paldea;
        var regions = new List<Raid9RegionViewModel>();
        var save = _session.WorkingSave;
        foreach (var candidate in Enum.GetValues<TeraRaidOrigin>())
        {
            if ((int)candidate > save.SaveRevision) continue;
            var data = candidate switch { TeraRaidOrigin.Kitakami => save.RaidKitakami,
                TeraRaidOrigin.BlueberryAcademy => save.RaidBlueberry, _ => save.RaidPaldea };
            if (data.CountAll < data.CountUsed) continue;
            var region = new Raid9RegionViewModel(candidate, data);
            region.Changed += ValidationChanged;
            foreach (var row in region.Raids) row.Changed += ValidationChanged;
            regions.Add(region);
        }
        Regions = regions;
        var stars = save.RaidSevenStar;
        var separate = stars.Defeated.Data.Length != 0 && save.AllBlocks.Any(b => b.Key == 0xA4BA4848 && b.Type != SCTypeCode.None);
        var count = separate ? Math.Min(stars.CountAll, stars.Defeated.CountAll) : stars.CountAll;
        RaidItems = Enumerable.Range(0, Math.Max(0, count)).Select(i => new RaidSevenStarItem(i, stars.GetRaid(i))).ToArray();
        OnPropertyChanged(nameof(Regions)); OnPropertyChanged(nameof(RaidItems)); OnPropertyChanged(nameof(IsSupported));
        SelectedRegion = Regions.FirstOrDefault(r => r.Origin == origin) ?? Regions.FirstOrDefault();
        SelectedRecord = RaidItems.FirstOrDefault();
        ValidationChanged();
    }
    private void DetachRows()
    {
        foreach (var region in Regions)
        {
            region.Changed -= ValidationChanged;
            foreach (var row in region.Raids) row.Changed -= ValidationChanged;
        }
    }
    [RelayCommand] private void Reset()
    {
        _session?.Reset(); StatusText = string.Empty; ReloadRows();
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private void Save()
    {
        if (_session is null || !CanSave) return;
        if (!_session.TryCommit(out _)) { StatusText = LocalizedStrings.Instance["TeraSession_StorageChanged"]; return; }
        _closed = true; ValidationChanged(); CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() { _closed = true; ValidationChanged(); CloseRequested?.Invoke(); }
    [RelayCommand(CanExecute = nameof(CanCopy))] private async Task CopyToOthersAsync()
    {
        if (!CanCopy || SelectedRegion is null || SelectedRaid is null || _dialogs is null) return;
        var region = SelectedRegion;
        var index = SelectedRaid.Index;
        var seed = IncludeSeed;
        var working = _session!.WorkingSave;
        if (!await _dialogs.ShowConfirmationAsync(LocalizedStrings.Instance["Raid9Editor_CopyToOthers"],
            LocalizedStrings.Instance.Format(seed ? "TeraSession_CopySeedConfirm" : "TeraSession_CopyConfirm", region.Name),
            LocalizedStrings.Instance["Raid9Editor_CopyToOthers"], LocalizedStrings.Instance["Common_Cancel"])) return;
        if (_closed || !ReferenceEquals(_session!.WorkingSave, working) || !ReferenceEquals(SelectedRegion, region)) return;
        region.Data.Propagate(index, seed);
        foreach (var row in region.Raids) row.Refresh();
        Filter(region.Raids[index]);
    }
    private void RefreshLanguage()
    {
        foreach (var region in Regions) region.RefreshLanguage();
        foreach (var record in RaidItems) record.RefreshLanguage();
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Region));
        ValidationChanged(); Filter(SelectedRaid);
    }
    public void Dispose()
    {
        _closed = true; ValidationChanged(); DetachRows(); WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

public sealed class Raid9RegionViewModel : ViewModelBase
{
    public TeraRaidOrigin Origin { get; }
    public string Name => LocalizedStrings.Instance[$"TeraSession_Region{(int)Origin}"];
    public RaidSpawnList9 Data { get; }
    public IReadOnlyList<Raid9ViewModel> Raids { get; }
    public Raid9ViewModel? SelectedRaid { get; set; }
    public bool HasSeeds => Data.HasSeeds;
    private string _currentSeedHex;
    private string _tomorrowSeedHex;
    public event Action? Changed;
    public bool HasSeedError => SeedError.Length != 0;
    public string SeedError => Valid(_currentSeedHex) && Valid(_tomorrowSeedHex) ? string.Empty : LocalizedStrings.Instance["TeraSession_Seed64Error"];
    public string CurrentSeedHex { get => _currentSeedHex; set => SetSeed(value, false); }
    public string TomorrowSeedHex { get => _tomorrowSeedHex; set => SetSeed(value, true); }
    public Raid9RegionViewModel(TeraRaidOrigin origin, RaidSpawnList9 data)
    {
        Origin = origin; Data = data;
        _currentSeedHex = data.CurrentSeed.ToString("X16"); _tomorrowSeedHex = data.TomorrowSeed.ToString("X16");
        Raids = Enumerable.Range(0, data.CountUsed).Select(i => new Raid9ViewModel(i, data.GetRaid(i))).ToArray();
    }
    private static bool Valid(string text) => text.Length == 16 && ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
    private void SetSeed(string value, bool tomorrow)
    {
        if (!HasSeeds) return;
        if (tomorrow) _tomorrowSeedHex = value; else _currentSeedHex = value;
        if (Valid(value))
        {
            var parsed = ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (tomorrow) Data.TomorrowSeed = parsed; else Data.CurrentSeed = parsed;
        }
        OnPropertyChanged(string.Empty); Changed?.Invoke();
    }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(SeedError));
        foreach (var row in Raids) row.RefreshLanguage();
    }
}

public sealed class Raid9ContentChoice(uint value) : ViewModelBase
{
    public int Value { get; } = unchecked((int)value);
    public string Text => value < 4 ? LocalizedStrings.Instance[$"TeraSession_Content{value}"]
        : LocalizedStrings.Instance.Format("TeraSession_UnknownContent", value);
    public void RefreshLanguage() => OnPropertyChanged(nameof(Text));
}

public sealed class Raid9ViewModel : ViewModelBase
{
    private readonly TeraRaidDetail _raid;
    private string _seedHex;
    public event Action? Changed;
    public int Index { get; }
    public string DisplayName => LocalizedStrings.Instance.Format("TeraSession_Raid", Index + 1);
    public string ScenePointName => _raid.ScenePointName;
    public string SearchText => $"{DisplayName} {ScenePointName} {SeedHex} {ContentName}";
    public ObservableCollection<Raid9ContentChoice> Contents { get; } = [];
    public string ContentName => Contents.First(c => c.Value == Content).Text;
    public bool IsEnabled { get => _raid.IsEnabled; set { if (value == IsEnabled) return; _raid.IsEnabled = value; Notify(); } }
    public uint Area { get => _raid.AreaID; set { if (value == Area) return; _raid.AreaID = value; Notify(); } }
    public uint LotteryGroup { get => _raid.LotteryGroup; set { if (value == LotteryGroup) return; _raid.LotteryGroup = value; Notify(); } }
    public uint SpawnPointId { get => _raid.SpawnPointID; set { if (value == SpawnPointId) return; _raid.SpawnPointID = value; Notify(); } }
    public bool LeaguePointsClaimed { get => _raid.IsClaimedLeaguePoints; set { if (value == LeaguePointsClaimed) return; _raid.IsClaimedLeaguePoints = value; Notify(); } }
    public int Content { get => unchecked((int)(uint)_raid.Content); set { if (value == Content) return; _raid.Content = (TeraRaidContentType)unchecked((uint)value); EnsureChoice(); Notify(); } }
    public uint Seed { get => _raid.Seed; set { _raid.Seed = value; Refresh(); } }
    public string SeedError { get; private set; } = string.Empty;
    public bool HasSeedError => SeedError.Length != 0;
    public string SeedHex
    {
        get => _seedHex;
        set
        {
            _seedHex = value;
            if (value.Length == 8 && uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            { _raid.Seed = parsed; SeedError = string.Empty; }
            else SeedError = LocalizedStrings.Instance["TeraSession_Seed32Error"];
            Notify();
        }
    }
    public Raid9ViewModel(int index, TeraRaidDetail raid)
    {
        Index = index; _raid = raid; _seedHex = raid.Seed.ToString("X8");
        foreach (var value in Enumerable.Range(0, 4)) Contents.Add(new Raid9ContentChoice((uint)value));
        EnsureChoice();
    }
    private void EnsureChoice() { if (!Contents.Any(c => c.Value == Content)) Contents.Add(new Raid9ContentChoice((uint)_raid.Content)); }
    private void Notify() { OnPropertyChanged(string.Empty); Changed?.Invoke(); }
    public void Refresh() { _seedHex = Seed.ToString("X8"); SeedError = string.Empty; EnsureChoice(); Notify(); }
    public void RefreshLanguage()
    {
        foreach (var choice in Contents) choice.RefreshLanguage();
        if (SeedError.Length != 0) SeedError = LocalizedStrings.Instance["TeraSession_Seed32Error"];
        OnPropertyChanged(string.Empty);
    }
}

public sealed class RaidSevenStarItem(int index, SevenStarRaidDetail raid) : ViewModelBase
{
    public int Index { get; } = index;
    public string DisplayName => LocalizedStrings.Instance.Format("TeraSession_Record", Index + 1, Identifier);
    public uint Identifier { get => raid.Identifier; set { if (value == Identifier) return; raid.Identifier = value; OnPropertyChanged(string.Empty); } }
    public bool Captured { get => raid.Captured; set { if (value == Captured) return; raid.Captured = value; OnPropertyChanged(nameof(Captured)); } }
    public bool Defeated { get => raid.Defeated; set { if (value == Defeated) return; raid.Defeated = value; OnPropertyChanged(nameof(Defeated)); } }
    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayName));
}
