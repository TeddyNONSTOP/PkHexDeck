using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Misc7bEditorViewModel
{
    [ObservableProperty] private int _selectedArea;
    [ObservableProperty] private GoParkSlotRow? _selectedSlot;
    [ObservableProperty] private ObservableCollection<GoParkSlotRow> _parkSlots = [];
    [ObservableProperty] private bool _wholePark;
    [ObservableProperty] private IReadOnlyList<ComboItem> _areas = [];
    public bool HasOccupiedSelection => !_closed && SelectedSlot?.Occupied == true;
    public string SlotDetails
    {
        get
        {
            if (SelectedSlot is not { Occupied: true } row) return T("LgpeTrainer_EmptySlot");
            var value = _session.Staged.Park[row.Index];
            if (value.Species >= GameInfo.Strings.Species.Count) return row.Name;
            return value.Dump(GameInfo.Strings.Species, row.Index);
        }
    }
    public int GoParkCount => GoParkStorage.Count;
    public int GoParkSlotCount => GoParkStorage.SlotsPerArea;
    public int GoParkAreaCount => GoParkStorage.Areas;
    partial void OnSelectedAreaChanged(int value) { if (!_refreshingLanguage && value is >= 0 and < GoParkStorage.Areas) RefreshPark(); }
    partial void OnSelectedSlotChanged(GoParkSlotRow? value)
    { OnPropertyChanged(nameof(HasOccupiedSelection)); OnPropertyChanged(nameof(SlotDetails)); }
    private void RefreshPark()
    {
        int selected = SelectedSlot?.Index ?? Math.Clamp(SelectedArea, 0, GoParkStorage.Areas - 1) * GoParkStorage.SlotsPerArea;
        int start = Math.Clamp(SelectedArea, 0, GoParkStorage.Areas - 1) * GoParkStorage.SlotsPerArea;
        var rows = new List<GoParkSlotRow>();
        for (int index = start; index < start + GoParkStorage.SlotsPerArea; index++)
        {
            var record = _session.Staged.Park[index]; ushort species = record.Species;
            string name = species == 0 ? T("LgpeTrainer_EmptySlot") : species < GameInfo.Strings.Species.Count
                ? GameInfo.Strings.Species[species] : LocalizedStrings.Instance.Format("LgpeTrainer_UnknownValue", species);
            rows.Add(new(index, index - start + 1, name, species == 0 ? null : record.LevelF, species == 0 ? null : record.CP, species != 0));
        }
        ParkSlots = new(rows); SelectedSlot = ParkSlots.FirstOrDefault(row => row.Index == selected) ?? ParkSlots.FirstOrDefault();
        OnPropertyChanged(nameof(SlotDetails)); OnPropertyChanged(nameof(HasOccupiedSelection));
    }
    private int[] Scope() => Enumerable.Range(WholePark ? 0 : SelectedArea * GoParkStorage.SlotsPerArea,
        WholePark ? GoParkStorage.Count : GoParkStorage.SlotsPerArea).ToArray();
    private bool Active(int epoch) => !_closed && epoch == _epoch;
    private void ParkChanged() { _epoch++; Error = string.Empty; RefreshPark(); NotifyState(); }
    private bool ValidRecord(byte[] data)
    {
        if (data.Length != GP1.SIZE) return false;
        var record = GP1.FromData(data);
        return record.Species is > 0 and <= 809 && _session.Staged.Personal.IsSpeciesInGame(record.Species) &&
            float.IsFinite(record.LevelF) && record.LevelF is >= 1 and <= 100 && record.CP >= 0;
    }
    [RelayCommand] private async Task ImportSlotAsync()
    {
        if (_closed || _dialogs is null || SelectedSlot is not { } slot) return; int epoch = _epoch;
        var path = await _dialogs.OpenFileAsync(T("LgpeTrainer_ImportSlot"), ["gp1"]);
        if (path is null || !Active(epoch)) return;
        try
        {
            if (new FileInfo(path).Length != GP1.SIZE) { Error = T("LgpeTrainer_InvalidRecord"); return; }
            var data = await File.ReadAllBytesAsync(path);
            if (!Active(epoch)) return;
            if (!ValidRecord(data)) { Error = T("LgpeTrainer_InvalidRecord"); return; }
            if (slot.Occupied && (!await _dialogs.ShowConfirmationAsync(T("LgpeTrainer_ImportSlot"),
                LocalizedStrings.Instance.Format("LgpeTrainer_ConfirmReplace", slot.Index / GoParkStorage.SlotsPerArea + 1, slot.Number), T("LgpeTrainer_Apply"), T("Common_Cancel")) || !Active(epoch))) return;
            _session.ApplyAction(save => save.Park[slot.Index] = GP1.FromData(data)); ParkChanged();
        }
        catch (IOException) { Error = T("LgpeTrainer_FileError"); }
        catch (UnauthorizedAccessException) { Error = T("LgpeTrainer_FileError"); }
    }
    [RelayCommand] private async Task ExportSlotAsync()
    {
        if (!HasOccupiedSelection || _dialogs is null || SelectedSlot is not { } slot) return;
        var data = _session.Staged.Park[slot.Index].Data.ToArray();
        var path = await _dialogs.SaveFileAsync(T("LgpeTrainer_ExportSlot"), FileName(slot.Index), ["gp1"]);
        if (path is null) return;
        try { await File.WriteAllBytesAsync(path, data); }
        catch (IOException) { Error = T("LgpeTrainer_FileError"); }
        catch (UnauthorizedAccessException) { Error = T("LgpeTrainer_FileError"); }
    }
    [RelayCommand] private async Task DeleteSlotAsync()
    {
        if (!HasOccupiedSelection || _dialogs is null || SelectedSlot is not { } slot) return; int epoch = _epoch;
        if (!await _dialogs.ShowConfirmationAsync(T("LgpeTrainer_DeleteSlot"),
            LocalizedStrings.Instance.Format("LgpeTrainer_ConfirmDeleteSlot", slot.Index / GoParkStorage.SlotsPerArea + 1, slot.Number), T("LgpeTrainer_Apply"), T("Common_Cancel")) || !Active(epoch)) return;
        _session.ApplyAction(save => save.Park[slot.Index] = new GP1()); ParkChanged();
    }
    private static string FileName(int index) => $"park{index / GoParkStorage.SlotsPerArea + 1:00}-slot{index % GoParkStorage.SlotsPerArea + 1:00}.gp1";
    [RelayCommand] private async Task ImportFolderAsync()
    {
        if (_closed || _dialogs is null) return; int epoch = _epoch; var scope = Scope();
        var folder = await _dialogs.OpenFolderAsync(T("LgpeTrainer_ImportFolder"));
        if (folder is null || !Active(epoch)) return;
        try
        {
            var free = scope.Where(index => _session.Staged.Park[index].Species == 0).ToArray();
            var files = Directory.EnumerateFiles(folder).Where(path => path.EndsWith(".gp1", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(free.Length + 1).ToArray();
            if (files.Length == 0) { Error = T("LgpeTrainer_NoFiles"); return; }
            if (files.Length > free.Length) { Error = T("LgpeTrainer_NoCapacity"); return; }
            var records = new List<byte[]>();
            foreach (var file in files)
            {
                if (new FileInfo(file).Length != GP1.SIZE) { Error = T("LgpeTrainer_InvalidRecord"); return; }
                var data = await File.ReadAllBytesAsync(file);
                if (!Active(epoch)) return;
                if (!ValidRecord(data)) { Error = T("LgpeTrainer_InvalidRecord"); return; }
                records.Add(data);
            }
            if (!await _dialogs.ShowConfirmationAsync(T("LgpeTrainer_ImportFolder"),
                LocalizedStrings.Instance.Format("LgpeTrainer_ConfirmImportFolder", records.Count), T("LgpeTrainer_Apply"), T("Common_Cancel")) || !Active(epoch)) return;
            _session.ApplyAction(save => { for (int i = 0; i < records.Count; i++) save.Park[free[i]] = GP1.FromData(records[i]); }); ParkChanged();
        }
        catch (IOException) { Error = T("LgpeTrainer_FileError"); }
        catch (UnauthorizedAccessException) { Error = T("LgpeTrainer_FileError"); }
    }
    [RelayCommand] private async Task ExportFolderAsync()
    {
        if (_closed || _dialogs is null) return;
        var records = Scope().Where(index => _session.Staged.Park[index].Species != 0)
            .Select(index => (Name: FileName(index), Data: _session.Staged.Park[index].Data.ToArray())).ToArray();
        var folder = await _dialogs.OpenFolderAsync(T("LgpeTrainer_ExportFolder"));
        if (folder is null || records.Length == 0) return;
        try
        {
            if (records.Any(record => File.Exists(Path.Combine(folder, record.Name))) &&
                !await _dialogs.ShowConfirmationAsync(T("LgpeTrainer_ExportFolder"), T("LgpeTrainer_ConfirmOverwrite"), T("LgpeTrainer_Apply"), T("Common_Cancel"))) return;
            foreach (var record in records) await File.WriteAllBytesAsync(Path.Combine(folder, record.Name), record.Data);
        }
        catch (IOException) { Error = T("LgpeTrainer_ExportError"); }
        catch (UnauthorizedAccessException) { Error = T("LgpeTrainer_ExportError"); }
    }
    [RelayCommand] private async Task CopySummaryAsync()
    {
        if (_closed || _dialogs is null) return;
        var lines = Scope().Select(index => (Index: index, Record: _session.Staged.Park[index])).Where(item => item.Record.Species != 0)
            .Select(item => item.Record.Species < GameInfo.Strings.Species.Count ? item.Record.Dump(GameInfo.Strings.Species, item.Index)
                : LocalizedStrings.Instance.Format("LgpeTrainer_UnknownValue", item.Record.Species));
        await _dialogs.SetClipboardTextAsync(string.Join(Environment.NewLine, lines));
    }
}

public sealed record GoParkSlotRow(int Index, int Number, string Name, float? Level, int? Cp, bool Occupied);
