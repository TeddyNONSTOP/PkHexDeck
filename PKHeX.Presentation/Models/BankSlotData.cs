using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.Models;

/// <summary>
/// Data for a single Bank box slot, displayed in the <c>BankView</c> grid. Mirrors the shape of
/// <see cref="SlotData"/> (the save-scoped box grid) but is keyed by the Bank entry's own stable
/// <see cref="Id"/> rather than a slot index into a live <c>SaveFile</c>.
/// </summary>
public partial class BankSlotData : ObservableObject
{
    [ObservableProperty] private int _slot;
    [ObservableProperty] private Guid? _id;
    [ObservableProperty] private ushort _species;
    [ObservableProperty] private byte[]? _sprite;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Marked for a bulk action in the Bank window's multi-select mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isMarked;
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private byte _level;
    [ObservableProperty] private int _heldItem;
    [ObservableProperty] private string _heldItemName = string.Empty;
    [ObservableProperty] private bool _isEgg;
    [ObservableProperty] private byte _form;
    [ObservableProperty] private string _format = string.Empty;
    [ObservableProperty] private string _sourceLabel = string.Empty;
    [ObservableProperty] private DateTimeOffset _addedUtc;

    /// <summary>The entry is held by a withdrawal into, or a deposit from, an open save that has not been written yet.</summary>
    [ObservableProperty] private bool _isPending;

    /// <summary>The hold is a deposit (otherwise a withdrawal); only meaningful while <see cref="IsPending"/>.</summary>
    [ObservableProperty] private bool _isPendingDeposit;

    /// <summary>Which save the pending withdrawal went into, or the pending deposit came from (display only).</summary>
    [ObservableProperty] private string _pendingSaveLabel = string.Empty;

    /// <summary>The entry's raw-bytes file is missing on disk (e.g. after restoring an older index).</summary>
    [ObservableProperty] private bool _isDataMissing;

    public bool IsEmpty => Id is null;

    /// <summary>One-based slot number shown in the storage tray.</summary>
    public int SlotNumber => Slot + 1;

    /// <summary>True when the entry can be withdrawn right now.</summary>
    public bool CanWithdraw => !IsEmpty && !IsPending && !IsDataMissing;

    public string ToolTipSummary
    {
        get
        {
            var loc = LocalizedStrings.Instance;
            if (IsEmpty)
                return loc["Bank_SlotEmpty"];

            var lines = new List<string>
            {
                IsEgg
                    ? loc.Format("Bank_SlotTooltipEgg", SpeciesName)
                    : loc.Format("Bank_SlotTooltip", DisplayName, Level) + (IsShiny ? " ★" : string.Empty),
            };
            if (!string.IsNullOrEmpty(HeldItemName))
                lines.Add(loc.Format("Bank_SlotTooltipItem", HeldItemName));
            lines.Add(loc.Format("Bank_SlotTooltipSource", SourceLabel, Format.ToUpperInvariant()));
            if (IsPending)
                lines.Add(loc.Format(IsPendingDeposit ? "Bank_SlotTooltipPendingDeposit" : "Bank_SlotTooltipPending", PendingSaveLabel));
            if (IsDataMissing)
                lines.Add(loc["Bank_SlotTooltipMissing"]);
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Concise, screen-reader-friendly announcement for this slot.</summary>
    public string AccessibleName
    {
        get
        {
            var loc = LocalizedStrings.Instance;
            if (IsEmpty)
                return loc.Format("Bank_SlotAccessibleEmpty", SlotNumber);

            var text = IsEgg
                ? loc.Format("Bank_SlotAccessibleEgg", SlotNumber, SpeciesName)
                : loc.Format("Bank_SlotAccessible", SlotNumber, DisplayName, Level);
            if (IsShiny && !IsEgg)
                text += ", " + loc["Bank_SlotShiny"];
            if (IsPending)
                text += ", " + loc[IsPendingDeposit ? "Bank_SlotPendingDepositShort" : "Bank_SlotPendingShort"];
            if (IsDataMissing)
                text += ", " + loc["Bank_SlotMissingShort"];
            if (IsMarked)
                text += ", " + loc["Organizer_MarkedOverlay"];
            return text;
        }
    }

    /// <summary>Nickname when set, otherwise the species name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? SpeciesName : Nickname;
}
