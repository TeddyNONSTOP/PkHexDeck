using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Modal preview shown before Pokémon from another format (a Bank entry, a dropped file, a slot
/// dragged from another window) are written into the open save: what the conversion changes and
/// whether the result is legal. Nothing is written here; the caller writes only when
/// <see cref="Confirmed"/> is <see langword="true"/> after the dialog closes.
/// </summary>
public partial class TransferPreviewViewModel : ViewModelBase, ICloseableDialog
{
    public TransferPreviewViewModel(IReadOnlyList<EntityTransferPreview> previews, string targetLabel, string confirmText, ISpriteRenderer? sprites = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(previews.Count);
        var loc = LocalizedStrings.Instance;
        Items = previews.Select(p => new TransferPreviewItem(p, sprites)).ToArray();
        ConfirmText = confirmText;

        var illegal = Items.Count(i => !i.ResultLegal);
        var losing = Items.Count(i => i.LosesLegality);
        SummaryText = Items.Count == 1
            ? loc.Format("TransferPreview_SummaryOne", Items[0].Name, targetLabel)
            : loc.Format("TransferPreview_SummaryMany", Items.Count, targetLabel);
        WarningText = losing > 0
            ? loc.Format("TransferPreview_WarningLosesLegality", losing)
            : illegal > 0 ? loc.Format("TransferPreview_WarningIllegal", illegal) : string.Empty;
        Selected = Items[0];
    }

    public IReadOnlyList<TransferPreviewItem> Items { get; }

    /// <summary>More than one Pokémon: the list is shown beside the details.</summary>
    public bool ShowList => Items.Count > 1;

    public string SummaryText { get; }
    public string WarningText { get; }
    public bool HasWarning => WarningText.Length > 0;
    public string ConfirmText { get; }

    [ObservableProperty] private TransferPreviewItem? _selected;

    /// <summary><see langword="true"/> once the user chose to go ahead; stays <see langword="false"/> when cancelled or closed.</summary>
    public bool Confirmed { get; private set; }

    public Action? CloseRequested { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke();
    }
}

/// <summary>One Pokémon in a <see cref="TransferPreviewViewModel"/>.</summary>
public sealed class TransferPreviewItem
{
    private readonly ISpriteRenderer? _sprites;
    private byte[]? _sprite;
    private bool _spriteLoaded;

    public TransferPreviewItem(EntityTransferPreview preview, ISpriteRenderer? sprites)
    {
        Preview = preview;
        _sprites = sprites;
        var loc = LocalizedStrings.Instance;
        var result = preview.Result;
        Name = result.IsNicknamed && !string.IsNullOrWhiteSpace(result.Nickname)
            ? result.Nickname
            : StringResourceLookup.Species(result.Species);
        FormatText = preview.FormatChanged
            ? loc.Format("TransferPreview_FormatChange", preview.Source.GetType().Name, result.GetType().Name)
            : loc.Format("TransferPreview_FormatSame", result.GetType().Name);
        LegalityText = preview.ResultLegal
            ? loc["TransferPreview_Legal"]
            : preview.LosesLegality ? loc["TransferPreview_LosesLegality"] : loc["TransferPreview_Illegal"];
        Changes = preview.Changes.Select(c => new TransferPreviewChangeRow(
            loc[$"TransferPreview_Field_{c.Field}"],
            Display(c, c.Before),
            Display(c, c.After))).ToArray();
        ListText = loc.Format("TransferPreview_ListItem", Name, Changes.Count);
    }

    public EntityTransferPreview Preview { get; }
    public string Name { get; }
    public string FormatText { get; }
    public string ListText { get; }
    public string LegalityText { get; }
    public bool ResultLegal => Preview.ResultLegal;
    public bool LosesLegality => Preview.LosesLegality;
    public string LegalityReport => Preview.LegalityReport;
    public IReadOnlyList<TransferPreviewChangeRow> Changes { get; }
    public bool HasChanges => Changes.Count > 0;

    public byte[]? Sprite
    {
        get
        {
            if (!_spriteLoaded)
            {
                _spriteLoaded = true;
                _sprite = _sprites?.GetSprite(Preview.Result);
            }
            return _sprite;
        }
    }

    private static string Display(EntityTransferChange change, string value)
    {
        if (change.IsFlag)
            return LocalizedStrings.Instance[bool.TryParse(value, out var flag) && flag ? "TransferPreview_Yes" : "TransferPreview_No"];
        return value.Length == 0 ? LocalizedStrings.Instance["TransferPreview_None"] : value;
    }
}

/// <summary>One changed property: localized field name and the value before and after.</summary>
public sealed record TransferPreviewChangeRow(string Field, string Before, string After);
