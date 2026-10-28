using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using System.Collections.ObjectModel;

namespace PKHeX.Presentation.ViewModels;

public partial class BdspTrainerEditorViewModel
{
    [ObservableProperty] private uint _bp;
    [ObservableProperty] private DateTimeOffset? _startedDate;
    [ObservableProperty] private TimeSpan? _startedTime;
    [ObservableProperty] private int _startedSeconds;
    [ObservableProperty] private DateTimeOffset? _lastSavedDate;
    [ObservableProperty] private TimeSpan? _lastSavedTime;
    [ObservableProperty] private int _lastSavedSeconds;
    public ObservableCollection<BdspTrainerRecordViewModel> TrainerRecords { get; } = [];
    [ObservableProperty] private BdspTrainerRecordViewModel? _selectedRecord;
    private static DateTime? ReadLocal(long ticks)
    {
        if (ticks == 0) return null;
        try { return DateTime.FromFileTimeUtc(ticks).ToLocalTime(); } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static DateTimeOffset? LocalDate(DateTime? value) => value is { } date ? new DateTimeOffset(date.Date) : null;
    private static DateTime? Combine(DateTimeOffset? date, TimeSpan? time, int seconds, long originalTicks) =>
        date is { } d && time is { } t ? DateTime.SpecifyKind(d.Date.AddHours(t.Hours).AddMinutes(t.Minutes).AddSeconds(seconds).AddTicks(ReadLocal(originalTicks) is null ? 0 : originalTicks % TimeSpan.TicksPerSecond), DateTimeKind.Local) : null;
    private void LoadTimeRecords()
    {
        var save = _session.Staged; Bp = save.BattleTower.BP;
        var start = ReadLocal(save.System.TicksStart); StartedDate = LocalDate(start); StartedTime = start?.TimeOfDay; StartedSeconds = start?.Second ?? 0;
        var last = ReadLocal(save.System.TicksLatest); LastSavedDate = LocalDate(last); LastSavedTime = last?.TimeOfDay; LastSavedSeconds = last?.Second ?? 0;
        TrainerRecords.Clear();
        foreach (var record in Record8b.RecordList_8b) TrainerRecords.Add(new(record.Key, record.Value, save.GetRecord(record.Key), save.GetRecordMax(record.Key)));
        SelectedRecord = TrainerRecords.FirstOrDefault();
    }
    private static bool ValidTime(DateTimeOffset? date, TimeSpan? time, int seconds, long originalTicks)
    {
        if (date.HasValue != time.HasValue || seconds is < 0 or > 59) return false;
        if (date is null) return ReadLocal(originalTicks) is null;
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) return false;
        try { _ = Combine(date, time, seconds, originalTicks)!.Value.ToFileTimeUtc(); return true; } catch (ArgumentOutOfRangeException) { return false; }
    }
    private bool ValidTimeRecords => (Bp == _baseline.BattleTower.BP || Bp <= 9999) &&
        ValidTime(StartedDate, StartedTime, StartedSeconds, _baseline.System.TicksStart) &&
        ValidTime(LastSavedDate, LastSavedTime, LastSavedSeconds, _baseline.System.TicksLatest) && TrainerRecords.All(record => record.IsValid);
    private void ApplyTimeRecords(SAV8BS save)
    {
        if (Bp != _baseline.BattleTower.BP) save.BattleTower.BP = Bp;
        var started = Combine(StartedDate, StartedTime, StartedSeconds, _baseline.System.TicksStart);
        if (started is { } start && start != ReadLocal(_baseline.System.TicksStart)) save.System.LocalTimestampStart = start;
        var saved = Combine(LastSavedDate, LastSavedTime, LastSavedSeconds, _baseline.System.TicksLatest);
        if (saved is { } last && last != ReadLocal(_baseline.System.TicksLatest)) save.System.LocalTimestampLatest = last;
        foreach (var record in TrainerRecords) if (record.Value != record.Original) save.SetRecord(record.Id, record.Value);
    }
}

public partial class BdspTrainerRecordViewModel(int id, string name, int value, int maximum) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int Original { get; } = value;
    public int Maximum { get; } = maximum;
    public int DisplayMinimum => Math.Min(0, Original);
    public int DisplayMaximum => Math.Max(Maximum, Original);
    public bool IsValid => Value == Original || Value >= 0 && Value <= Maximum;
    [ObservableProperty] private int _value = value;
}
