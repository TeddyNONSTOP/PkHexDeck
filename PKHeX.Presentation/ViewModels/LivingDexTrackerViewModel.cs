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

/// <summary>Which Living Dex tracker rows are shown.</summary>
public enum LivingDexTrackerFilter
{
    All,
    Missing,
    Owned,
    NoShiny,
}

/// <summary>
/// Living Dex tracker: a read-only list of every species (or form) the save's game can hold, showing
/// which are owned, how many, which are shiny and where they are stored. Shown in a modeless tool window.
/// </summary>
public partial class LivingDexTrackerViewModel : ViewModelBase
{
    private const int LocationsShown = 3;

    private readonly SaveFile _sav;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly IBankService? _bank;
    private readonly LivingDexTrackerUseCase _useCase = new();
    private LivingDexTrackerRow[] _allRows = [];
    private bool _isLoading;

    public LivingDexTrackerViewModel(SaveFile sav, ISpriteRenderer spriteRenderer, IBankService? bank = null)
    {
        _sav = sav;
        _spriteRenderer = spriteRenderer;
        _bank = bank;
        Refresh();
    }

    /// <summary>Raised when the user asks to see an owned Pokémon; carries the save slot to show.</summary>
    public event Action<LivingDexHolding>? HoldingActivated;

    /// <summary>Raised when the user asks to arrange the boxes into dex order (the Living Dex autopilot).</summary>
    public event Action? ArrangeRequested;

    [RelayCommand]
    private void Arrange() => ArrangeRequested?.Invoke();

    public bool CanIncludeBank => _bank is not null;

    [ObservableProperty] private bool _includeForms;
    [ObservableProperty] private bool _includeBank;
    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<LivingDexTrackerRow> _rows = [];
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _eggText = string.Empty;
    [ObservableProperty] private double _ownedPercent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSelectedCommand))]
    private LivingDexTrackerRow? _selectedRow;

    public LivingDexTrackerFilter Filter => (LivingDexTrackerFilter)Math.Clamp(FilterIndex, 0, 3);

    partial void OnIncludeFormsChanged(bool value) => Refresh();
    partial void OnIncludeBankChanged(bool value) => Refresh();
    partial void OnFilterIndexChanged(int value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Re-reads the save (and Bank) and rebuilds the list. Edits made since opening appear after a refresh.</summary>
    [RelayCommand]
    public void Refresh()
    {
        if (_isLoading)
            return;
        _isLoading = true;
        try
        {
            IEnumerable<LivingDexHolding> holdings = LivingDexTrackerUseCase.ReadSave(_sav).ToList();
            if (IncludeBank && _bank is not null)
                holdings = holdings.Concat(LivingDexTrackerUseCase.ReadBank(_bank));

            var report = _useCase.Build(_sav, IncludeForms, holdings);
            var formNames = new Dictionary<ushort, string[]>();
            _allRows = report.Entries
                .Select(e => new LivingDexTrackerRow(e, GetName(e, formNames), DescribeLocations(e), _spriteRenderer, _sav.Context))
                .ToArray();

            OwnedPercent = report.Total == 0 ? 0 : 100.0 * report.OwnedCount / report.Total;
            SummaryText = LocalizedStrings.Instance.Format("LivingDexTracker_Summary",
                report.OwnedCount, report.Total, Math.Floor(OwnedPercent).ToString(CultureInfo.CurrentCulture), report.ShinyCount);
            EggText = report.EggCount == 0 ? string.Empty : LocalizedStrings.Instance.Format("LivingDexTracker_EggsNotCounted", report.EggCount);
        }
        finally
        {
            _isLoading = false;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = SelectedRow;
        var search = SearchText.Trim();
        var rows = _allRows.Where(r => Filter switch
        {
            LivingDexTrackerFilter.Missing => !r.IsOwned,
            LivingDexTrackerFilter.Owned => r.IsOwned,
            LivingDexTrackerFilter.NoShiny => !r.HasShiny,
            _ => true,
        });
        if (search.Length != 0)
            rows = rows.Where(r => r.Matches(search));

        Rows = new ObservableCollection<LivingDexTrackerRow>(rows);
        SelectedRow = selected is null ? null : Rows.FirstOrDefault(r => r.Species == selected.Species && r.Form == selected.Form);
    }

    private bool CanShowSelected => SelectedRow?.SaveHolding is not null;

    /// <summary>Shows the selected row's first Pokémon stored in the save (party first, then boxes).</summary>
    [RelayCommand(CanExecute = nameof(CanShowSelected))]
    private void ShowSelected()
    {
        if (SelectedRow?.SaveHolding is { } holding)
            HoldingActivated?.Invoke(holding);
    }

    private string GetName(LivingDexTrackerEntry entry, Dictionary<ushort, string[]> formNames)
    {
        var strings = GameInfo.Strings;
        var name = entry.Species < strings.Species.Count ? strings.Species[entry.Species] : entry.Species.ToString(CultureInfo.InvariantCulture);
        if (entry.Form == 0)
            return name;

        if (!formNames.TryGetValue(entry.Species, out var forms))
            formNames[entry.Species] = forms = FormConverter.GetFormList(entry.Species, strings.types, strings.forms, GameInfo.GenderSymbolUnicode, _sav.Context);
        return entry.Form < forms.Length && !string.IsNullOrWhiteSpace(forms[entry.Form])
            ? $"{name} ({forms[entry.Form]})"
            : $"{name} ({entry.Form})";
    }

    private string DescribeLocations(LivingDexTrackerEntry entry)
    {
        if (entry.Count == 0)
            return LocalizedStrings.Instance["LivingDexTracker_Missing"];

        var shown = entry.Holdings.Take(LocationsShown).Select(DescribeLocation);
        var text = string.Join("; ", shown);
        return entry.Count > LocationsShown
            ? $"{text}; {LocalizedStrings.Instance.Format("LivingDexTracker_More", entry.Count - LocationsShown)}"
            : text;
    }

    private string DescribeLocation(LivingDexHolding holding) => holding.Source switch
    {
        LivingDexSourceKind.Party => LocalizedStrings.Instance.Format("LivingDexTracker_InParty", holding.Slot + 1),
        LivingDexSourceKind.Bank => LocalizedStrings.Instance.Format("LivingDexTracker_InBank", holding.Box + 1, holding.Slot + 1),
        _ => LocalizedStrings.Instance.Format("LivingDexTracker_InBox", GetBoxName(holding.Box), holding.Slot + 1),
    };

    private string GetBoxName(int box) => _sav is IBoxDetailNameRead names
        ? names.GetBoxName(box)
        : BoxDetailNameExtensions.GetDefaultBoxName(box);
}

/// <summary>One row of the Living Dex tracker.</summary>
public sealed class LivingDexTrackerRow
{
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly EntityContext _context;
    private byte[]? _sprite;
    private bool _spriteLoaded;

    public LivingDexTrackerRow(LivingDexTrackerEntry entry, string name, string locations, ISpriteRenderer spriteRenderer, EntityContext context)
    {
        Species = entry.Species;
        Form = entry.Form;
        Name = name;
        Count = entry.Count;
        ShinyCount = entry.ShinyCount;
        Locations = locations;
        // Party and box slots can be shown in the app; Bank entries cannot.
        SaveHolding = entry.Holdings.Where(h => h.Source != LivingDexSourceKind.Bank).Select(h => (LivingDexHolding?)h).FirstOrDefault();
        _spriteRenderer = spriteRenderer;
        _context = context;
    }

    public ushort Species { get; }
    public byte Form { get; }
    public string Number => Species.ToString("0000", CultureInfo.InvariantCulture);
    public string Name { get; }
    public int Count { get; }
    public int ShinyCount { get; }
    public bool IsOwned => Count > 0;
    public bool HasShiny => ShinyCount > 0;

    /// <summary>Missing species show a faded sprite.</summary>
    public double SpriteOpacity => IsOwned ? 1.0 : 0.35;
    public string Locations { get; }
    public LivingDexHolding? SaveHolding { get; }

    /// <summary>Sprite PNG, shiny when a shiny is owned. Rendered on first use, so only visible rows pay for it.</summary>
    public byte[]? Sprite
    {
        get
        {
            if (!_spriteLoaded)
            {
                _spriteLoaded = true;
                try { _sprite = _spriteRenderer.GetSprite(Species, Form, 0, 0, HasShiny, _context); }
                catch { _sprite = null; }
            }
            return _sprite;
        }
    }

    public bool Matches(string search)
    {
        if (ushort.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number == Species;
        return Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}
