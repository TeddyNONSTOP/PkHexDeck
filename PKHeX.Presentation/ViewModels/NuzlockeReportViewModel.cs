using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Which Nuzlocke report rows are shown.</summary>
public enum NuzlockeReportFilter
{
    All,
    Extras,
    Dupes,
}

/// <summary>
/// Nuzlocke report: a read-only list of the save's encounter locations in the order they were reached,
/// showing which catch counts at each one, the extra catches, and dupes. Shown in a modeless tool window.
/// </summary>
public partial class NuzlockeReportViewModel : ViewModelBase
{
    private readonly SaveFile _sav;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly NuzlockeReportUseCase _useCase = new();
    private NuzlockeReportRow[] _allRows = [];

    public NuzlockeReportViewModel(SaveFile sav, ISpriteRenderer spriteRenderer)
    {
        _sav = sav;
        _spriteRenderer = spriteRenderer;
        Refresh();
    }

    /// <summary>Raised when the user asks to see a row's encounter; carries the save slot to show.</summary>
    public event Action<LivingDexHolding>? HoldingActivated;

    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<NuzlockeReportRow> _rows = [];
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _notCountedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSelectedCommand))]
    private NuzlockeReportRow? _selectedRow;

    public NuzlockeReportFilter Filter => (NuzlockeReportFilter)Math.Clamp(FilterIndex, 0, 2);

    partial void OnFilterIndexChanged(int value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Re-reads the save and rebuilds the list. Changes made since opening appear after a refresh.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var report = _useCase.Build(_sav);
        _allRows = report.Locations
            .Select((location, i) => new NuzlockeReportRow(i + 1, location, NameOf, _spriteRenderer, _sav.Context))
            .ToArray();

        var encounters = report.Locations.Count(l => l.Counted is not null);
        SummaryText = LocalizedStrings.Instance.Format("Nuzlocke_Summary",
            encounters, report.Locations.Count, report.Locations.Count(l => l.FirstIsDupe), report.Locations.Count(l => l.Extras.Any()));

        var notCounted = new List<string>();
        if (report.OtherTrainers.Count != 0)
            notCounted.Add(LocalizedStrings.Instance.Format("Nuzlocke_OtherTrainers", report.OtherTrainers.Count));
        if (report.EggCount != 0)
            notCounted.Add(LocalizedStrings.Instance.Format("Nuzlocke_EggsNotCounted", report.EggCount));
        NotCountedText = string.Join(" ", notCounted);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = SelectedRow;
        var search = SearchText.Trim();
        var rows = _allRows.Where(r => Filter switch
        {
            NuzlockeReportFilter.Extras => r.HasExtras,
            NuzlockeReportFilter.Dupes => r.HasDupe,
            _ => true,
        });
        if (search.Length != 0)
            rows = rows.Where(r => r.Matches(search));

        Rows = new ObservableCollection<NuzlockeReportRow>(rows);
        SelectedRow = selected is null ? null : Rows.FirstOrDefault(r => r.Location == selected.Location);
    }

    private bool CanShowSelected => SelectedRow?.Holding is not null;

    /// <summary>Shows the selected location's counted encounter in the box or party.</summary>
    [RelayCommand(CanExecute = nameof(CanShowSelected))]
    private void ShowSelected()
    {
        if (SelectedRow?.Holding is { } holding)
            HoldingActivated?.Invoke(holding);
    }

    private string NameOf(NuzlockeCatch entry)
    {
        var strings = GameInfo.Strings;
        var species = entry.Species < strings.Species.Count ? strings.Species[entry.Species] : entry.Species.ToString(CultureInfo.InvariantCulture);
        return !entry.IsNicknamed || string.IsNullOrWhiteSpace(entry.Nickname)
            ? LocalizedStrings.Instance.Format("Nuzlocke_PokemonLevel", species, entry.Level)
            : LocalizedStrings.Instance.Format("Nuzlocke_NicknamedLevel", entry.Nickname, species, entry.Level);
    }
}

/// <summary>One location of the Nuzlocke report.</summary>
public sealed class NuzlockeReportRow
{
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly EntityContext _context;
    private readonly ushort _spriteSpecies;
    private readonly byte _spriteForm;
    private byte[]? _sprite;
    private bool _spriteLoaded;

    public NuzlockeReportRow(int order, NuzlockeLocation location, Func<NuzlockeCatch, string> nameOf,
        ISpriteRenderer spriteRenderer, EntityContext context)
    {
        Order = order;
        Location = location.Name;
        var counted = location.Counted;
        Encounter = counted is null ? LocalizedStrings.Instance["Nuzlocke_NoEncounter"] : nameOf(counted);
        HasDupe = location.FirstIsDupe;
        Status = (counted, HasDupe) switch
        {
            (null, _) => LocalizedStrings.Instance.Format("Nuzlocke_Status_OnlyDupes", nameOf(location.First)),
            (_, true) => LocalizedStrings.Instance.Format("Nuzlocke_Status_Dupe", nameOf(location.First)),
            _ when counted.Origin == NuzlockeOrigin.Egg => LocalizedStrings.Instance["Nuzlocke_Status_Egg"],
            _ => LocalizedStrings.Instance["Nuzlocke_Status_Counts"],
        };
        var extras = location.Extras.ToArray();
        HasExtras = extras.Length != 0;
        Extras = string.Join(", ", extras.Select(nameOf));
        Holding = counted is null ? null : new LivingDexHolding(counted.Species, counted.Form, false, false,
            counted.Box < 0 ? LivingDexSourceKind.Party : LivingDexSourceKind.Box, Math.Max(counted.Box, 0), counted.Slot);
        var shown = counted ?? location.First;
        (_spriteSpecies, _spriteForm) = (shown.Species, shown.Form);
        _spriteRenderer = spriteRenderer;
        _context = context;
    }

    public int Order { get; }
    public string Location { get; }
    public string Encounter { get; }
    public string Status { get; }
    public string Extras { get; }
    public bool HasExtras { get; }
    public bool HasDupe { get; }
    public LivingDexHolding? Holding { get; }

    /// <summary>Sprite PNG of the counted encounter. Rendered on first use, so only visible rows pay for it.</summary>
    public byte[]? Sprite
    {
        get
        {
            if (!_spriteLoaded)
            {
                _spriteLoaded = true;
                try { _sprite = _spriteRenderer.GetSprite(_spriteSpecies, _spriteForm, 0, 0, false, _context); }
                catch { _sprite = null; }
            }
            return _sprite;
        }
    }

    public bool Matches(string search)
        => Location.Contains(search, StringComparison.CurrentCultureIgnoreCase)
           || Encounter.Contains(search, StringComparison.CurrentCultureIgnoreCase)
           || Extras.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
