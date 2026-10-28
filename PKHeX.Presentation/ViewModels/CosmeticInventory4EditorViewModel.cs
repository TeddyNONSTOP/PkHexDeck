using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Stages the three Gen 4 cosmetic inventories without touching the source save.</summary>
public partial class CosmeticInventory4EditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SAV4 _source;
    public CosmeticInventory4EditorViewModel(SAV4 save)
    {
        _source = save;
        Categories = [
            new(0, "Cosmetic4_Seals", (int)Seal4.MAX, i => save.GetSealCount((Seal4)i),
                _ => SAV4.SealMaxCount, i => i < (int)Seal4.MAXLEGAL),
            new(1, "Cosmetic4_Accessories", AccessoryInfo.Count, i => save.GetAccessoryOwnedCount((Accessory4)i),
                i => i <= AccessoryInfo.MaxMulti ? AccessoryInfo.AccessoryMaxCount : 1, i => i <= AccessoryInfo.MaxLegal),
            new(2, "Cosmetic4_Backdrops", BackdropInfo.Count, i => save.GetBackdropPosition((Backdrop4)i),
                _ => BackdropInfo.Count, i => i <= (int)BackdropInfo.MaxLegal),
        ];
        foreach (var row in Categories.SelectMany(c => c.Entries))
            row.PropertyChanged += RowChanged;
        _selectedCategory = Categories[0];
        RefreshRows();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }

    public Action? CloseRequested { get; set; }
    public IReadOnlyList<CosmeticInventory4Category> Categories { get; }
    [ObservableProperty] private CosmeticInventory4Category _selectedCategory;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<CosmeticInventory4Row> _visibleEntries = [];
    public bool CanSave => Categories.SelectMany(c => c.Entries).All(r => !r.IsChanged || r.Value is >= 0 && r.Value <= r.Maximum)
        && (!Categories[2].Entries.Any(r => r.IsChanged) || Categories[2].Entries
            .Where(r => r.Value < BackdropInfo.Count).GroupBy(r => r.Value).All(g => g.Count() == 1));
    public string ScopeText => LocalizedStrings.Instance.Format("Cosmetic4_Scope", SelectedCategory.Name, SelectedCategory.Entries.Count);
    public string ValueHeader => LocalizedStrings.Instance[SelectedCategory.Kind == 2 ? "Cosmetic4_Position" : "InventoryEditor_Count"];
    public string ValueHelp => LocalizedStrings.Instance[SelectedCategory.Kind == 2 ? "Cosmetic4_PositionHelp" : "Cosmetic4_CountHelp"];
    public string ValidationText => CanSave ? string.Empty : LocalizedStrings.Instance["Cosmetic4_Invalid"];
    partial void OnSelectedCategoryChanged(CosmeticInventory4Category value)
    {
        RefreshRows();
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(ValueHeader));
        OnPropertyChanged(nameof(ValueHelp));
    }
    partial void OnSearchTextChanged(string value) => RefreshRows();
    private void RefreshRows() => VisibleEntries = SelectedCategory.Entries.Where(r => string.IsNullOrWhiteSpace(SearchText)
        || r.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
        || r.Index.ToString().Contains(SearchText, StringComparison.Ordinal)).ToArray();
    private void RefreshLanguage()
    {
        foreach (var category in Categories) category.RefreshLanguage();
        RefreshRows();
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(ValueHeader));
        OnPropertyChanged(nameof(ValueHelp));
        OnPropertyChanged(nameof(ValidationText));
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CosmeticInventory4Row.Value)) return;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ValidationText));
        SaveCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand] private void ClearCategory()
    {
        foreach (var row in SelectedCategory.Entries)
            row.Value = SelectedCategory.Kind == 2 ? BackdropInfo.Count : 0;
    }
    [RelayCommand] private void GiveLegal() => Give(false);
    [RelayCommand] private void GiveIncludingUnreleased() => Give(true);
    private void Give(bool unreleased)
    {
        // Scope is the entire selected category, including rows hidden by search.
        foreach (var row in SelectedCategory.Entries)
        {
            if (!unreleased && !row.IsLegal) continue;
            row.Value = SelectedCategory.Kind == 2 ? row.Index : row.Maximum;
        }
        if (SelectedCategory.Kind == 2)
        {
            // Retain already-owned unreleased backdrops, but keep positions unique
            // when legal entries are inserted ahead of them.
            foreach (var row in SelectedCategory.Entries.Where(r => !r.IsLegal && r.Value < BackdropInfo.Count))
                row.Value = row.Index;
        }
    }
    [RelayCommand] private void Reset()
    {
        foreach (var row in Categories.SelectMany(c => c.Entries)) row.Value = row.OriginalValue;
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private void Save()
    {
        if (!CanSave) return;
        foreach (var category in Categories)
        foreach (var row in category.Entries.Where(r => r.IsChanged))
        {
            switch (category.Kind)
            {
                case 0: _source.SetSealCount((Seal4)row.Index, (byte)row.Value); break;
                case 1: _source.SetAccessoryOwnedCount((Accessory4)row.Index, (byte)row.Value); break;
                case 2: _source.SetBackdropPosition((Backdrop4)row.Index, (byte)row.Value); break;
            }
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
}

public class CosmeticInventory4Category : ObservableObject
{
    private readonly string _nameKey;
    public int Kind { get; }
    public string Name => LocalizedStrings.Instance[_nameKey];
    public IReadOnlyList<CosmeticInventory4Row> Entries { get; }
    public CosmeticInventory4Category(int kind, string nameKey, int count, Func<int, byte> read,
        Func<int, int> maximum, Func<int, bool> legal)
    {
        Kind = kind;
        _nameKey = nameKey;
        Entries = Enumerable.Range(0, count).Select(i => new CosmeticInventory4Row(kind, i, read(i), maximum(i), legal(i))).ToArray();
    }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        foreach (var row in Entries) row.RefreshLanguage();
    }
}

public partial class CosmeticInventory4Row : ObservableObject
{
    private readonly int _kind;
    public int Index { get; }
    public int Maximum { get; }
    public int OriginalValue { get; }
    public bool IsLegal { get; }
    public bool IsChanged => Value != OriginalValue;
    public string Name
    {
        get
        {
            var names = _kind switch { 0 => GameInfo.Strings.seals, 1 => GameInfo.Strings.accessories, _ => GameInfo.Strings.backdrops };
            return Index < names.Length ? names[Index] : Index.ToString();
        }
    }
    public string Status => LocalizedStrings.Instance[IsLegal ? "Cosmetic4_Released" : "Cosmetic4_Unreleased"];
    public CosmeticInventory4Row(int kind, int index, byte value, int maximum, bool legal)
    {
        _kind = kind;
        Index = index;
        _value = OriginalValue = value;
        Maximum = maximum;
        IsLegal = legal;
    }
    [ObservableProperty] private int _value;
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Status));
    }
}
