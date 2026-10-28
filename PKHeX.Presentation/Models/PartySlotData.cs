using CommunityToolkit.Mvvm.ComponentModel;

namespace PKHeX.Presentation.Models;

/// <summary>
/// Data for a single party slot, displayed in the PartyViewer list.
/// </summary>
public partial class PartySlotData : ObservableObject
{
    public SlotLocation Location => SlotLocation.FromParty(Slot);
    public int SlotNumber => Slot + 1;
    
    [ObservableProperty] private int _slot;
    [ObservableProperty] private ushort _species;
    [ObservableProperty] private byte[]? _sprite;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isSelected;
    /// <summary>Whether this slot is part of the active multi-select "mark" set (Organizer bulk actions), independent of <see cref="IsSelected"/>'s single-slot cursor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isMarked;
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private byte _level;
    [ObservableProperty] private byte _gender; // 0=Male, 1=Female, 2=Genderless
    [ObservableProperty] private ushort _heldItem;
    [ObservableProperty] private string _heldItemName = string.Empty;
    [ObservableProperty] private bool _isEgg;
    [ObservableProperty] private ushort _currentHp;
    [ObservableProperty] private ushort _maxHp;
    [ObservableProperty] private byte _status; // Status condition
    [ObservableProperty] private bool _isLegal = true;
    [ObservableProperty] private string _showdownSummary = string.Empty;
    public string SemanticSummary { get; init; } = string.Empty;
    
    /// <summary>
    /// HP percentage for display.
    /// </summary>
    public double HpPercentage => MaxHp > 0 ? (double)CurrentHp / MaxHp : 0;
    
    public string GenderSymbol => Gender switch
    {
        0 => "♂",
        1 => "♀",
        _ => ""
    };
    
    /// <summary>
    /// Short summary for tooltip.
    /// </summary>
    public string ToolTipSummary => IsEmpty
        ? "Empty"
        : string.Join(Environment.NewLine, new[] { SemanticSummary, ShowdownSummary }.Where(s => s.Length != 0));

    /// <summary>
    /// Concise, screen-reader-friendly announcement for this slot, e.g.
    /// "Party slot 2: Pikachu, Lv. 25, shiny" or "Party slot 4: Empty".
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var header = $"Party slot {Slot + 1}";
            if (IsMarked)
                header += $" ({PKHeX.Presentation.Localization.LocalizedStrings.Instance["Organizer_MarkedOverlay"]})";
            if (IsEmpty)
                return $"{header}: Empty";

            var parts = new System.Collections.Generic.List<string>
            {
                SpeciesName,
                $"Lv. {Level}",
            };
            if (IsEgg && SemanticSummary.Length == 0)
                parts.Add(PKHeX.Presentation.Localization.LocalizedStrings.Instance["SlotState_Egg"]);
            if (IsShiny && SemanticSummary.Length == 0)
                parts.Add("shiny");
            if (!IsLegal && SemanticSummary.Length == 0)
                parts.Add("illegal");
            if (SemanticSummary.Length != 0) parts.Add(SemanticSummary);
            if (MaxHp > 0 && CurrentHp == 0)
                parts.Add("fainted");

            return $"{header}: {string.Join(", ", parts)}";
        }
    }
}
