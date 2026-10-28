using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Complete, staged Let's Go trainer and GO Complex workflow.</summary>
public partial class Misc7bEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly TrainerBlockDataSession<SAV7b> _session;
    private readonly IDialogService? _dialogs;
    private SAV7b _baseline;
    private bool _closed;
    private bool _refreshingLanguage;
    private int _epoch;
    public Action? CloseRequested { get; set; }
    public bool CanUndo => !_closed && _session.CanUndo;
    public bool HasError => Error.Length != 0;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private string _trainerName = string.Empty;
    [ObservableProperty] private string _rivalName = string.Empty;
    [ObservableProperty] private uint _money;
    [ObservableProperty] private int _gender;
    [ObservableProperty] private int _language;
    [ObservableProperty] private int _gameVersion;
    [ObservableProperty] private uint _displayTid;
    [ObservableProperty] private uint _displaySid;
    [ObservableProperty] private int _playedHours;
    [ObservableProperty] private int _playedMinutes;
    [ObservableProperty] private int _playedSeconds;
    [ObservableProperty] private ulong _mapId;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _scaleX;
    [ObservableProperty] private double _scaleY;
    [ObservableProperty] private double _scaleZ;
    [ObservableProperty] private double _rotation;
    [ObservableProperty] private DateTimeOffset? _adventureDate;
    [ObservableProperty] private TimeSpan? _adventureTime;
    [ObservableProperty] private int _adventureSecond;
    [ObservableProperty] private DateTimeOffset? _lastSavedDate;
    [ObservableProperty] private TimeSpan? _lastSavedTime;
    public ObservableCollection<ComboItem> Languages { get; } = [];
    public ObservableCollection<ComboItem> Versions { get; } = [];
    public IReadOnlyList<ComboItem> Genders => [new(T("Pokedex5Editor_Male"), 0), new(T("Pokedex5Editor_Female"), 1)];
    public uint MaxDisplaySid => (uint.MaxValue - Math.Min(DisplayTid, 999999)) / 1_000_000;
    public bool CanSave => !_closed && IsInputValid;
    public bool IsInputValid => TrainerName.Length <= 12 && RivalName.Length <= 12 &&
        _session.Staged.IsValidTrainerID7(DisplaySid, DisplayTid) &&
        (Gender == _baseline.Gender || Gender is 0 or 1) &&
        (Language == _baseline.Language || Languages.Any(item => item.Value == Language)) &&
        (GameVersion == (int)_baseline.Version || GameVersion is (int)PKHeX.Core.GameVersion.GP or (int)PKHeX.Core.GameVersion.GE) &&
        (Money == _baseline.Money || Money <= 9_999_999) && PlayedHours is >= 0 and <= ushort.MaxValue &&
        (PlayedMinutes == _baseline.PlayedMinutes || PlayedMinutes is >= 0 and <= 59) &&
        (PlayedSeconds == _baseline.PlayedSeconds || PlayedSeconds is >= 0 and <= 59) &&
        ValidFloat(X, _baseline.Coordinates.X) && ValidFloat(Y, _baseline.Coordinates.Y) && ValidFloat(Z, _baseline.Coordinates.Z) &&
        ValidFloat(ScaleX, _baseline.Coordinates.SX) && ValidFloat(ScaleY, _baseline.Coordinates.SY) && ValidFloat(ScaleZ, _baseline.Coordinates.SZ) &&
        (Rotation.Equals(ReadRotation(_baseline)) || double.IsFinite(Rotation)) &&
        ValidDate(AdventureDate, AdventureTime, 1970) && AdventureSecond is >= 0 and <= 59 &&
        ValidDate(LastSavedDate, LastSavedTime, 1900);

    public Misc7bEditorViewModel(SAV7b save, IDialogService? dialogs = null)
    {
        _session = new(save); _dialogs = dialogs; _baseline = (SAV7b)save.Clone();
        LoadData();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((Misc7bEditorViewModel)recipient).RefreshLanguage());
    }
    private static bool ValidFloat(double value, float original) => value.Equals((double)original) || (double.IsFinite(value) && Math.Abs(value) <= float.MaxValue);
    private static bool ValidDate(DateTimeOffset? date, TimeSpan? time, int minimumYear) =>
        date.HasValue == time.HasValue && (date is null || date.Value.Year >= minimumYear) &&
        (time is null || time.Value >= TimeSpan.Zero && time.Value < TimeSpan.FromDays(1));
    private static double ReadRotation(SAV7b save) => Math.Atan2(save.Coordinates.RZ, save.Coordinates.RW) * 360 / Math.PI;
    private static string T(string key) => LocalizedStrings.Instance[key];
    private void LoadData()
    {
        var save = _session.Staged; _baseline = (SAV7b)save.Clone();
        TrainerName = save.OT; RivalName = save.Misc.RivalName; Money = save.Money;
        Gender = save.Gender; Language = save.Language; GameVersion = (int)save.Version;
        DisplayTid = save.DisplayTID; DisplaySid = save.DisplaySID;
        PlayedHours = save.PlayedHours; PlayedMinutes = save.PlayedMinutes; PlayedSeconds = save.PlayedSeconds;
        MapId = save.Coordinates.M; X = save.Coordinates.X; Y = save.Coordinates.Y; Z = save.Coordinates.Z;
        ScaleX = save.Coordinates.SX; ScaleY = save.Coordinates.SY; ScaleZ = save.Coordinates.SZ; Rotation = ReadRotation(save);
        AdventureDate = null; AdventureTime = null; AdventureSecond = 0;
        try { var value = save.PlayerGeoLocation.AdventureBegin.Timestamp; AdventureDate = new(value); AdventureTime = value.TimeOfDay; AdventureSecond = value.Second; }
        catch (ArgumentOutOfRangeException) { }
        var saved = save.Played.LastSavedDate;
        LastSavedDate = saved is { } date ? new(date) : null; LastSavedTime = saved?.TimeOfDay;
        RefreshLanguage(); Error = string.Empty; NotifyState();
    }
    public void RefreshLanguage()
    {
        int language = Language, version = GameVersion, area = SelectedArea;
        _refreshingLanguage = true;
        Languages.Clear(); foreach (var item in GameInfo.Sources.LanguageDataSource(7, EntityContext.Gen7b)) Languages.Add(item);
        Versions.Clear(); Versions.Add(new(T("LgpeTrainer_Pikachu"), (int)PKHeX.Core.GameVersion.GP)); Versions.Add(new(T("LgpeTrainer_Eevee"), (int)PKHeX.Core.GameVersion.GE));
        AddUnknown(Languages, _baseline.Language); AddUnknown(Versions, (int)_baseline.Version);
        Areas = Enumerable.Range(0, GoParkStorage.Areas).Select(index => new ComboItem(LocalizedStrings.Instance.Format("LgpeTrainer_Area", index + 1), index)).ToArray();
        Language = language; GameVersion = version;
        SelectedArea = area;
        _refreshingLanguage = false;
        OnPropertyChanged(nameof(Language)); OnPropertyChanged(nameof(GameVersion));
        OnPropertyChanged(nameof(Genders)); RefreshPark();
    }
    private static void AddUnknown(ObservableCollection<ComboItem> choices, int value)
    { if (!choices.Any(item => item.Value == value)) choices.Add(new(LocalizedStrings.Instance.Format("LgpeTrainer_UnknownValue", value), value)); }
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(CanSave) or nameof(IsInputValid) or nameof(CanUndo) or nameof(MaxDisplaySid)))
        { base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CanSave))); base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsInputValid))); }
        if (args.PropertyName == nameof(DisplayTid)) base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(MaxDisplaySid)));
    }
    private void ApplyFields(SAV7b save)
    {
        var original = _baseline;
        if (TrainerName != original.OT) save.OT = TrainerName;
        if (RivalName != original.Misc.RivalName) save.Misc.RivalName = RivalName;
        if (Money != original.Money) save.Money = Money;
        if (Gender != original.Gender) save.Gender = (byte)Gender;
        if (Language != original.Language) save.Language = Language;
        if (GameVersion != (int)original.Version) save.Version = (PKHeX.Core.GameVersion)GameVersion;
        if (DisplayTid != original.DisplayTID || DisplaySid != original.DisplaySID) save.SetDisplayID(DisplayTid, DisplaySid);
        if (PlayedHours != original.PlayedHours) save.PlayedHours = PlayedHours;
        if (PlayedMinutes != original.PlayedMinutes) save.PlayedMinutes = PlayedMinutes;
        if (PlayedSeconds != original.PlayedSeconds) save.PlayedSeconds = PlayedSeconds;
        if (MapId != original.Coordinates.M) save.Coordinates.M = MapId;
        if (!X.Equals((double)original.Coordinates.X)) save.Coordinates.X = (float)X;
        if (!Y.Equals((double)original.Coordinates.Y)) save.Coordinates.Y = (float)Y;
        if (!Z.Equals((double)original.Coordinates.Z)) save.Coordinates.Z = (float)Z;
        if (!ScaleX.Equals((double)original.Coordinates.SX)) save.Coordinates.SX = (float)ScaleX;
        if (!ScaleY.Equals((double)original.Coordinates.SY)) save.Coordinates.SY = (float)ScaleY;
        if (!ScaleZ.Equals((double)original.Coordinates.SZ)) save.Coordinates.SZ = (float)ScaleZ;
        if (Math.Abs(Rotation - ReadRotation(original)) > 0.00000001)
        { double angle = Rotation * Math.PI / 360; save.Coordinates.RX = 0; save.Coordinates.RY = 0; save.Coordinates.RZ = (float)Math.Sin(angle); save.Coordinates.RW = (float)Math.Cos(angle); }
        if (AdventureDate is { } adventure && AdventureTime is { } time)
        {
            var value = adventure.Date + new TimeSpan(time.Hours, time.Minutes, AdventureSecond);
            DateTime? initial = null; try { initial = original.PlayerGeoLocation.AdventureBegin.Timestamp; } catch (ArgumentOutOfRangeException) { }
            if (value != initial) save.PlayerGeoLocation.AdventureBegin.Timestamp = value;
        }
        if (LastSavedDate is { } saved && LastSavedTime is { } savedTime)
        { var value = saved.Date + new TimeSpan(savedTime.Hours, savedTime.Minutes, 0); if (value != original.Played.LastSavedDate) save.Played.LastSavedDate = value; }
    }
    [RelayCommand] private void MaxMoney() => Money = 9_999_999;
    [RelayCommand] private void Save()
    {
        if (!CanSave) return;
        if (!_session.TryCommit(ApplyFields)) { Error = T("LgpeTrainer_Conflict"); return; }
        _epoch++;
        if (CloseRequested is { } close) { _closed = true; close(); Dispose(); } else LoadData();
    }
    [RelayCommand] private void Reset() { if (_closed) return; _epoch++; _session.Reset(); LoadData(); }
    [RelayCommand] private void Cancel() { if (CloseRequested is { } close) { _closed = true; close(); Dispose(); } else Reset(); }
    [RelayCommand] private void Undo() { if (_closed) return; _epoch++; _session.Undo(); RefreshPark(); NotifyState(); }
    [RelayCommand] private Task DeleteAllGoPark() => Bulk("DeleteAll");
    [RelayCommand] private Task UnlockAllTrainerTitles() => Bulk("Titles");
    [RelayCommand] private Task UnlockAllFashion() => Bulk("Fashion");
    private async Task Bulk(string kind)
    {
        if (_closed || _dialogs is null) return; int epoch = _epoch;
        if (!await _dialogs.ShowConfirmationAsync(T("LgpeTrainer_Action" + kind), T("LgpeTrainer_Confirm" + kind), T("LgpeTrainer_Apply"), T("Common_Cancel")) || _closed || epoch != _epoch) return;
        _session.ApplyAction(save =>
        {
            if (kind == "DeleteAll") save.Park.DeleteAll();
            else if (kind == "Titles") save.EventWork.UnlockAllTitleFlags();
            else { save.Blocks.FashionPlayer.UnlockAllAccessoriesPlayer(); save.Blocks.FashionStarter.UnlockAllAccessoriesStarter(); }
        });
        _epoch++; RefreshPark(); NotifyState();
    }
    private void NotifyState() { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanUndo)); }
    public void Dispose() { _closed = true; _epoch++; WeakReferenceMessenger.Default.UnregisterAll(this); NotifyState(); }
}
