using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;

namespace PKHeX.Presentation.ViewModels;

/// <summary>One selectable wallpaper (a flat color or a bundled box-art image) in the picker.</summary>
public partial class WallpaperChoice : ObservableObject
{
    public required BankWallpaper Wallpaper { get; init; }

    /// <summary>Hex color for a <see cref="BankWallpaperKind.Color"/> choice.</summary>
    public string? ColorHex { get; init; }

    /// <summary>PNG bytes for a <see cref="BankWallpaperKind.Art"/> choice.</summary>
    public byte[]? Image { get; init; }

    public required string AccessibleName { get; init; }

    public bool IsColor => ColorHex is not null;
    public bool IsArt => Image is not null;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>A titled group of art wallpapers (one game's box art).</summary>
public sealed record WallpaperGroup(string Name, IReadOnlyList<WallpaperChoice> Items);

/// <summary>
/// Picker for a Bank box's wallpaper: the flat <see cref="BankWallpaperPalette"/> colors plus every
/// bundled native box-art image exposed by <see cref="IBoxWallpaperCatalog"/> (the same per-game
/// wallpaper art the save editors ship). Purely decorative — the result is applied by the caller.
/// </summary>
public partial class WallpaperPickerViewModel : ViewModelBase, ICloseableDialog
{
    public WallpaperPickerViewModel(IBoxWallpaperCatalog catalog, BankWallpaper current)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var loc = LocalizedStrings.Instance;

        for (var i = 0; i < BankWallpaperPalette.Colors.Count; i++)
        {
            Colors.Add(new WallpaperChoice
            {
                Wallpaper = BankWallpaper.Color(i),
                ColorHex = BankWallpaperPalette.Colors[i],
                AccessibleName = loc.Format("Bank_WallpaperColorName", i + 1),
            });
        }

        foreach (var group in catalog.GetCatalog().GroupBy(a => a.GroupName))
        {
            var items = new List<WallpaperChoice>();
            var index = 0;
            foreach (var asset in group)
            {
                index++;
                var bytes = catalog.GetImageBytes(asset.Id);
                if (bytes is null)
                    continue;
                items.Add(new WallpaperChoice
                {
                    Wallpaper = BankWallpaper.Art(asset.Id),
                    Image = bytes,
                    AccessibleName = loc.Format("Bank_WallpaperArtName", group.Key, index),
                });
            }
            if (items.Count > 0)
                ArtGroups.Add(new WallpaperGroup(group.Key, items));
        }

        Selected = AllChoices.FirstOrDefault(c => c.Wallpaper == current);
    }

    public ObservableCollection<WallpaperChoice> Colors { get; } = [];
    public ObservableCollection<WallpaperGroup> ArtGroups { get; } = [];

    private IEnumerable<WallpaperChoice> AllChoices => Colors.Concat(ArtGroups.SelectMany(g => g.Items));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private WallpaperChoice? _selected;

    /// <summary>The chosen wallpaper once the dialog closed via Apply/Use default; <see langword="null"/> if cancelled.</summary>
    public BankWallpaper? Result { get; private set; }

    public Action? CloseRequested { get; set; }

    partial void OnSelectedChanged(WallpaperChoice? value)
    {
        foreach (var choice in AllChoices)
            choice.IsSelected = ReferenceEquals(choice, value);
    }

    [RelayCommand]
    private void Select(WallpaperChoice? choice)
    {
        if (choice is not null)
            Selected = choice;
    }

    private bool CanApply => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (Selected is null)
            return;
        Result = Selected.Wallpaper;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void UseDefault()
    {
        Result = BankWallpaper.Default;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke();
    }
}
