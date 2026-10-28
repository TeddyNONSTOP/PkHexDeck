using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Transactional save-level editor for Gen 3 and Gen 4 gift ribbon descriptions.</summary>
public partial class GiftRibbonEditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SaveFile _sourceSave;
    private readonly IGiftRibbons? _sourceBlock;

    public GiftRibbonEditorViewModel(SaveFile save)
    {
        _sourceSave = save;
        Ribbons = [];

        if (save is not IGiftRibbons || save.Generation is not (3 or 4))
        {
            IsSupported = false;
            Generation = save.Generation;
            return;
        }

        _sourceBlock = (IGiftRibbons)save;
        IsSupported = true;
        Generation = save.Generation;
        var values = _sourceBlock.GiftRibbons.ToArray();
        var indexes = IGiftRibbons.Index;
        for (int i = 0; i < values.Length && i < indexes.Length; i++)
        {
            var ribbon = new GiftRibbonItemViewModel(indexes[i], values[i]);
            ribbon.PropertyChanged += OnRibbonPropertyChanged;
            Ribbons.Add(ribbon);
        }

        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }

    public bool IsSupported { get; }
    public int Generation { get; }
    public string GenerationLabel => LocalizedStrings.Instance.Format("GiftRibbonEditor_Generation", Generation);
    public ObservableCollection<GiftRibbonItemViewModel> Ribbons { get; }
    public Action? CloseRequested { get; set; }

    public bool CanSave => IsSupported && Ribbons.All(ribbon => ribbon.DescriptionIndex is >= byte.MinValue and <= byte.MaxValue);

    private void OnRibbonPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GiftRibbonItemViewModel.DescriptionIndex))
            return;

        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLanguage()
    {
        OnPropertyChanged(nameof(GenerationLabel));
        foreach (var ribbon in Ribbons)
            ribbon.RefreshLanguage();
    }

    [RelayCommand]
    private void AllLegal()
    {
        if (_sourceBlock is null)
            return;

        foreach (var ribbon in Ribbons)
            ribbon.DescriptionIndex = 0;

        if (Generation == 3)
        {
            SetLegalDescription(RibbonIndex.Country, 32);
            SetLegalDescription(RibbonIndex.National, 44);
            SetLegalDescription(RibbonIndex.Earth, 45);
            SetLegalDescription(RibbonIndex.World, 32);
        }
        else if (Generation == 4)
        {
            SetLegalDescription(RibbonIndex.Classic, 64);
            SetLegalDescription(RibbonIndex.Premier, 59);
        }
    }

    private void SetLegalDescription(RibbonIndex ribbon, byte value)
    {
        var index = IGiftRibbons.Index.IndexOf(ribbon);
        var maximum = Generation == 3 ? IGiftRibbons.MAX_3 : IGiftRibbons.MAX_4;
        if (index < 0 || index >= Ribbons.Count || value > maximum)
            return;

        Ribbons[index].DescriptionIndex = value;
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (var ribbon in Ribbons)
            ribbon.DescriptionIndex = 0;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave || _sourceBlock is null)
            return;

        var values = _sourceBlock.GiftRibbons;
        for (int i = 0; i < Ribbons.Count; i++)
            values[i] = (byte)Ribbons[i].DescriptionIndex;

        _sourceSave.State.Edited = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}

public partial class GiftRibbonItemViewModel : ObservableObject
{
    public GiftRibbonItemViewModel(RibbonIndex ribbon, byte descriptionIndex)
    {
        Ribbon = ribbon;
        _descriptionIndex = descriptionIndex;
        RefreshLanguage();
    }

    public RibbonIndex Ribbon { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private int _descriptionIndex;

    public string DescriptionIndexAutomationName =>
        LocalizedStrings.Instance.Format("GiftRibbonEditor_DescriptionIndexFor", DisplayName);

    public void RefreshLanguage()
    {
        var propertyName = Ribbon.PropertyName;
        DisplayName = GameInfo.Strings.Ribbons.GetNameSafe(propertyName, out var localizedName)
            ? localizedName
            : propertyName.StartsWith("Ribbon", System.StringComparison.Ordinal) ? propertyName[6..] : propertyName;
        OnPropertyChanged(nameof(DescriptionIndexAutomationName));
    }
}
