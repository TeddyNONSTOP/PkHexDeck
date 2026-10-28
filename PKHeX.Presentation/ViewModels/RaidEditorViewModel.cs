using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>A single staged session across all available SWSH den regions.</summary>
public partial class RaidEditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SAV8SWSH? _source;
    public Action? CloseRequested { get; set; }
    public bool IsSupported => _source is not null && Regions.Count != 0;
    public IReadOnlyList<RaidRegionViewModel> Regions { get; private set; } = [];
    public IReadOnlyList<RaidViewModel> Dens => SelectedRegion?.Dens ?? [];
    public string Region => SelectedRegion?.Name ?? string.Empty;
    public bool CanSave => IsSupported && Regions.SelectMany(r => r.Dens).All(d => d.SeedError.Length == 0);
    private RaidRegionViewModel? _selectedRegion;
    public RaidRegionViewModel? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (ReferenceEquals(value, _selectedRegion)) return;
            var selection = value?.SelectedDen ?? value?.Dens.FirstOrDefault();
            SetProperty(ref _selectedRegion, value);
            // ListBox clears selection when its source changes. Publish the new source first,
            // then restore this region's selection so binding cannot erase the new detail row.
            OnPropertyChanged(nameof(Dens));
            OnPropertyChanged(nameof(Region));
            SelectedDen = selection;
        }
    }
    [ObservableProperty] private RaidViewModel? _selectedDen;

    public RaidEditorViewModel(SaveFile sav, string region = "Galar")
    {
        _source = sav as SAV8SWSH;
        Reload();
        SelectedRegion = Regions.FirstOrDefault(r => r.Origin == (region switch
        {
            "Isle of Armor" => MaxRaidOrigin.IsleOfArmor,
            "Crown Tundra" => MaxRaidOrigin.CrownTundra,
            _ => MaxRaidOrigin.Galar,
        })) ?? Regions.FirstOrDefault();
    }

    partial void OnSelectedDenChanged(RaidViewModel? value)
    {
        if (SelectedRegion is not null) SelectedRegion.SelectedDen = value;
    }

    private static RaidSpawnList8 GetRaids(SAV8SWSH save, MaxRaidOrigin origin) => origin switch
    {
        MaxRaidOrigin.IsleOfArmor => save.RaidArmor,
        MaxRaidOrigin.CrownTundra => save.RaidCrown,
        _ => save.RaidGalar,
    };

    private void Reload()
    {
        if (_source is null) return;
        var origin = SelectedRegion?.Origin ?? MaxRaidOrigin.Galar;
        var edited = _source.State.Edited;
        var working = (SAV8SWSH)_source.Clone();
        _source.State.Edited = edited;
        var regions = new List<RaidRegionViewModel>();
        foreach (var candidate in Enum.GetValues<MaxRaidOrigin>())
        {
            var raids = GetRaids(working, candidate);
            if (raids.Data.Length < raids.CountUsed * RaidSpawnDetail.SIZE) continue;
            var rows = Enumerable.Range(0, raids.CountUsed).Select(i => new RaidViewModel(i, raids.GetRaid(i))).ToArray();
            foreach (var row in rows) row.Changed += RaidChanged;
            regions.Add(new RaidRegionViewModel(candidate, raids, rows));
        }
        Regions = regions;
        OnPropertyChanged(nameof(Regions));
        SelectedRegion = Regions.FirstOrDefault(r => r.Origin == origin) ?? Regions.FirstOrDefault();
        RaidChanged();
    }
    private void RaidChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Explicitly discards every region's staged changes and reloads the committed source.</summary>
    [RelayCommand] private void Reset() => Reload();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_source is null || !CanSave) return;
        var changed = false;
        foreach (var region in Regions)
        {
            var source = GetRaids(_source, region.Origin).Data;
            var staged = region.Raids.Data;
            var before = region.Original.AsSpan();
            // Copy edited fields only; do not overwrite independently changed save data,
            // readonly table hashes, unused records or padding. Flag edits preserve unrelated bits.
            for (var index = 0; index < region.Dens.Count; index++)
            {
                var start = index * RaidSpawnDetail.SIZE;
                foreach (var (offset, length) in new[] { (8, 8), (16, 1), (17, 1), (18, 1) })
                {
                    var slice = staged.Slice(start + offset, length);
                    if (slice.SequenceEqual(before.Slice(start + offset, length))) continue;
                    if (!slice.SequenceEqual(source.Slice(start + offset, length)))
                    {
                        slice.CopyTo(source.Slice(start + offset, length));
                        changed = true;
                    }
                }
                var flagIndex = start + 19;
                var mask = before[flagIndex] ^ staged[flagIndex];
                var flags = (byte)((source[flagIndex] & ~mask) | (staged[flagIndex] & mask));
                if (source[flagIndex] == flags) continue;
                source[flagIndex] = flags;
                changed = true;
            }
        }
        if (changed) _source.State.Edited = true;
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
}

public sealed class RaidRegionViewModel(MaxRaidOrigin origin, RaidSpawnList8 raids, IReadOnlyList<RaidViewModel> dens)
{
    public MaxRaidOrigin Origin { get; } = origin;
    public string Name => LocalizedStrings.Instance[$"RaidSession_Region{(int)Origin}"];
    public RaidSpawnList8 Raids { get; } = raids;
    public byte[] Original { get; } = raids.Data.ToArray();
    public IReadOnlyList<RaidViewModel> Dens { get; } = dens;
    public RaidViewModel? SelectedDen { get; set; }
}

public partial class RaidViewModel : ViewModelBase
{
    private readonly RaidSpawnDetail _raid;
    private string _seedHex;
    public IReadOnlyList<ComboItem> DenTypes { get; }
    public event Action? Changed;
    public RaidViewModel(int index, RaidSpawnDetail raid)
    {
        Index = index;
        _raid = raid;
        _seedHex = raid.Seed.ToString("X16");
        DenTypes = Enumerable.Range(0, 7).Select(i => new ComboItem(LocalizedStrings.Instance[$"RaidSession_Type{i}"], i))
            .Concat(DenType > 6 ? [new ComboItem(LocalizedStrings.Instance.Format("RaidSession_UnknownType", DenType), DenType)] : Array.Empty<ComboItem>()).ToArray();
    }
    public int Index { get; }
    public string DisplayName => LocalizedStrings.Instance.Format("RaidSession_Den", Index + 1);
    public string Hash => _raid.Hash.ToString("X16");
    public bool IsActive => _raid.IsActive;
    public bool IsEvent { get => _raid.IsEvent; set { if (value == IsEvent) return; _raid.IsEvent = value; NotifyRaidChanged(); } }
    public bool IsRare { get => _raid.IsRare; set { if (value == IsRare) return; _raid.IsRare = value; NotifyRaidChanged(); } }
    public bool IsWishingPiece { get => _raid.IsWishingPiece; set { if (value == IsWishingPiece) return; _raid.IsWishingPiece = value; NotifyRaidChanged(); } }
    public bool WattsHarvested { get => _raid.WattsHarvested; set { if (value == WattsHarvested) return; _raid.WattsHarvested = value; NotifyRaidChanged(); } }
    public byte Stars { get => _raid.Stars; set { if (value == Stars) return; _raid.Stars = value; NotifyRaidChanged(); } }
    public byte RandRoll { get => _raid.RandRoll; set { if (value == RandRoll) return; _raid.RandRoll = value; NotifyRaidChanged(); } }
    public byte Flags { get => _raid.Flags; set { if (value == Flags) return; _raid.Flags = value; NotifyRaidChanged(); } }
    public int DenType
    {
        get => (int)_raid.DenType;
        set
        {
            if (value == DenType || value is < 0 or > 255) return;
            _raid.DenType = (RaidType)value;
            NotifyRaidChanged();
        }
    }
    public ulong Seed
    {
        get => _raid.Seed;
        set
        {
            if (value == Seed && SeedError.Length == 0) return;
            _raid.Seed = value;
            _seedHex = value.ToString("X16");
            SeedError = string.Empty;
            NotifyRaidChanged();
        }
    }
    [ObservableProperty] private string _seedError = string.Empty;
    public string SeedHex
    {
        get => _seedHex;
        set
        {
            if (value == _seedHex) return;
            _seedHex = value;
            if (value.Length == 16 && ulong.TryParse(value, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                _raid.Seed = parsed;
                SeedError = string.Empty;
            }
            else SeedError = LocalizedStrings.Instance["RaidSession_SeedError"];
            NotifyRaidChanged();
        }
    }
    private void NotifyRaidChanged()
    {
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }
    [RelayCommand] private void Deactivate() { _raid.Deactivate(); NotifyRaidChanged(); }
}
