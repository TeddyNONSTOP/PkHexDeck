using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using System.Globalization;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Misc8aEditorViewModel
{
    [ObservableProperty] private string _trainerName = string.Empty;
    [ObservableProperty] private int _gender;
    [ObservableProperty] private int _language;
    [ObservableProperty] private uint _displayTid;
    [ObservableProperty] private uint _displaySid;
    [ObservableProperty] private int _playedHours;
    [ObservableProperty] private int _playedMinutes;
    [ObservableProperty] private int _playedSeconds;
    [ObservableProperty] private string _mapName = string.Empty;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _rotation;
    [ObservableProperty] private DateTimeOffset? _startedDate;
    [ObservableProperty] private TimeSpan? _startedTime;
    [ObservableProperty] private int _startedSeconds;
    [ObservableProperty] private DateTimeOffset? _lastSavedDate;
    [ObservableProperty] private TimeSpan? _lastSavedTime;
    private readonly Dictionary<string, string> _positionText = new();
    private readonly HashSet<string> _invalidPosition = new();
    public IReadOnlyList<ComboItem> Genders => Options([new(LocalizedStrings.Instance["Pokedex5Editor_Male"], 0), new(LocalizedStrings.Instance["Pokedex5Editor_Female"], 1)], Gender);
    public IReadOnlyList<ComboItem> Languages => Options(GameInfo.Sources.LanguageDataSource(8, EntityContext.Gen8a), Language);
    private static IReadOnlyList<ComboItem> Options(IEnumerable<ComboItem> choices, int selected)
    {
        var result = choices.ToList();
        if (result.All(item => item.Value != selected)) result.Add(new(LocalizedStrings.Instance.Format("RaidSession_UnknownType", selected), selected));
        return result;
    }
    public void RefreshLanguage()
    {
        int gender = Gender, language = Language;
        OnPropertyChanged(nameof(Genders)); OnPropertyChanged(nameof(Languages));
        Gender = gender; Language = language;
    }
    public string XText { get => PositionText(nameof(X), X); set => SetPosition(nameof(X), value); }
    public string YText { get => PositionText(nameof(Y), Y); set => SetPosition(nameof(Y), value); }
    public string ZText { get => PositionText(nameof(Z), Z); set => SetPosition(nameof(Z), value); }
    private string PositionText(string key, double value) => _positionText.GetValueOrDefault(key) ?? value.ToString("R", CultureInfo.CurrentCulture);
    private void SetPosition(string key, string text)
    {
        _positionText[key] = text;
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value))
        {
            _invalidPosition.Remove(key);
            switch (key) { case nameof(X): X = value; break; case nameof(Y): Y = value; break; default: Z = value; break; }
        }
        else _invalidPosition.Add(key);
        OnPropertyChanged(key + "Text");
    }
    private static DateTime? ReadStarted(SAV8LA save)
    {
        if (save.AdventureStart.Seconds == 0) return null;
        try { return save.AdventureStart.Timestamp; } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static DateTime? ReadSaved(SAV8LA save)
    { try { return save.LastSaved.Timestamp; } catch (ArgumentOutOfRangeException) { return null; } }
    private static DateTimeOffset? Date(DateTime? value) => value is { } date ? new DateTimeOffset(date.Date, TimeSpan.Zero) : null;
    private static DateTime? Combine(DateTimeOffset? date, TimeSpan? time, int seconds = 0) =>
        date is { } d && time is { } t ? d.Date.AddHours(t.Hours).AddMinutes(t.Minutes).AddSeconds(seconds) : null;

    private void LoadTrainerFields()
    {
        _positionText.Clear(); _invalidPosition.Clear();
        TrainerName = _sav.OT; Gender = _sav.Gender; Language = _sav.Language;
        DisplayTid = _sav.DisplayTID; DisplaySid = _sav.DisplaySID;
        PlayedHours = _sav.PlayedHours; PlayedMinutes = _sav.PlayedMinutes; PlayedSeconds = _sav.PlayedSeconds;
        MapName = _sav.Coordinates.M; X = _sav.Coordinates.X; Y = _sav.Coordinates.Y; Z = _sav.Coordinates.Z;
        Rotation = Math.Atan2(_sav.Coordinates.RZ, _sav.Coordinates.RW) * 360 / Math.PI;
        var started = ReadStarted(_sav); StartedDate = Date(started); StartedTime = started?.TimeOfDay; StartedSeconds = started?.Second ?? 0;
        var saved = ReadSaved(_sav); LastSavedDate = Date(saved); LastSavedTime = saved?.TimeOfDay;
        foreach (var key in new[] { nameof(XText), nameof(YText), nameof(ZText) }) OnPropertyChanged(key);
    }

    public bool CanSave => !_closed && _invalidPosition.Count == 0 && TrainerName.Length <= 12 &&
        (Gender == _baseline.Gender || Gender is 0 or 1) && (Language == _baseline.Language || Language is >= 1 and <= 10 && Language != 6) &&
        DisplayTid <= 999999 && (ulong)DisplaySid * 1000000 + DisplayTid <= uint.MaxValue &&
        PlayedHours is >= 0 and <= 65535 && (PlayedMinutes == _baseline.PlayedMinutes || PlayedMinutes is >= 0 and <= 59) &&
        (PlayedSeconds == _baseline.PlayedSeconds || PlayedSeconds is >= 0 and <= 59) &&
        ValidProgress(SaveBlockAccessor8LA.KMoney, Money) && (Money == ReadProgress(_baseline, SaveBlockAccessor8LA.KMoney) || Money <= _baseline.MaxMoney) &&
        ValidProgress(SaveBlockAccessor8LA.KMeritCurrent, MeritCurrent) && ValidProgress(SaveBlockAccessor8LA.KMeritEarnedTotal, MeritEarned) &&
        ValidProgress(SaveBlockAccessor8LA.KSatchelUpgrades, SatchelUpgrades) && ValidProgress(SaveBlockAccessor8LA.KExpeditionTeamRank, Rank) &&
        (Rank == ReadProgress(_baseline, SaveBlockAccessor8LA.KExpeditionTeamRank) || Rank < PokedexConstants8a.ResearchPointsForRank.Length) &&
        (MapName == _baseline.Coordinates.M || MapName.Length < 0x48 && MapName.All(c => c is > '\0' and <= '\x7f')) &&
        ValidFloat(X, _baseline.Coordinates.X) && ValidFloat(Y, _baseline.Coordinates.Y) && ValidFloat(Z, _baseline.Coordinates.Z) &&
        (Rotation.Equals(Math.Atan2(_baseline.Coordinates.RZ, _baseline.Coordinates.RW) * 360 / Math.PI) || double.IsFinite(Rotation) && Math.Abs(Rotation) <= 360) &&
        ValidDate(StartedDate, StartedTime, ReadStarted(_baseline), 1970, 9999) && StartedSeconds is >= 0 and <= 59 &&
        ValidDate(LastSavedDate, LastSavedTime, ReadSaved(_baseline), 1900, 5995);
    private static bool ValidFloat(double value, float original) => value.Equals((double)original) || double.IsFinite(value) && Math.Abs(value) <= float.MaxValue;
    private static bool ValidDate(DateTimeOffset? date, TimeSpan? time, DateTime? original, int minYear, int maxYear) =>
        date.HasValue == time.HasValue && (date is null ? original is null : date.Value.Year >= minYear && date.Value.Year <= maxYear && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1));

    private void SaveTrainerFields()
    {
        if (TrainerName != _baseline.OT) _sav.OT = TrainerName;
        if (Gender != _baseline.Gender) _sav.Gender = (byte)Gender;
        if (Language != _baseline.Language) _sav.Language = Language;
        if (DisplayTid != _baseline.DisplayTID || DisplaySid != _baseline.DisplaySID) _sav.SetDisplayID(DisplayTid, DisplaySid);
        if (PlayedHours != _baseline.PlayedHours) _sav.PlayedHours = PlayedHours;
        if (PlayedMinutes != _baseline.PlayedMinutes) _sav.PlayedMinutes = PlayedMinutes;
        if (PlayedSeconds != _baseline.PlayedSeconds) _sav.PlayedSeconds = PlayedSeconds;
        if (MapName != _baseline.Coordinates.M) _sav.Coordinates.M = MapName;
        if (!X.Equals((double)_baseline.Coordinates.X)) _sav.Coordinates.X = (float)X;
        if (!Y.Equals((double)_baseline.Coordinates.Y)) _sav.Coordinates.Y = (float)Y;
        if (!Z.Equals((double)_baseline.Coordinates.Z)) _sav.Coordinates.Z = (float)Z;
        if (Math.Abs(Rotation - Math.Atan2(_baseline.Coordinates.RZ, _baseline.Coordinates.RW) * 360 / Math.PI) > 1e-8)
        {
            double angle = Rotation * Math.PI / 360;
            _sav.Coordinates.RX = 0; _sav.Coordinates.RY = 0; _sav.Coordinates.RZ = (float)Math.Sin(angle); _sav.Coordinates.RW = (float)Math.Cos(angle);
        }
        var started = Combine(StartedDate, StartedTime, StartedSeconds);
        if (started is { } start && start != ReadStarted(_baseline)) _sav.AdventureStart.Timestamp = start;
        var saved = Combine(LastSavedDate, LastSavedTime);
        if (saved is { } last && last != ReadSaved(_baseline)) _sav.LastSaved.Timestamp = last;
    }
}
