using PKHeX.Application.Abstractions;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public sealed record GiftPreviewRow(string Name, string Value);

public partial class MysteryGiftSlotViewModel
{
    private readonly ISpriteRenderer? _sprites;
    public byte[]? Sprite { get; private set; }
    public string CardIdentity { get; private set; } = string.Empty;
    public IReadOnlyList<GiftPreviewRow> PreviewRows { get; private set; } = [];

    private void RebuildPreview()
    {
        Sprite = null;
        CardIdentity = string.Empty;
        PreviewRows = [];
        var loc = LocalizedStrings.Instance;
        if (Gift is { IsEmpty: false } gift)
        {
            try
            {
                var preview = new ReadCardPreviewUseCase().Execute(gift);
                CardIdentity = loc.Format("GiftPreview_CardIdentity", preview.Id, preview.Title, preview.Format);
                var rows = new List<GiftPreviewRow> { new(loc["GiftPreview_Kind"], loc["GiftPreview_Kind_" + preview.Kind]) };
                foreach (var item in preview.Items)
                    rows.Add(new(loc["GiftPreview_Item"], $"{Name(GameInfo.Strings.Item, item.Id)} ×{item.Quantity}"));
                foreach (var field in preview.Fields)
                {
                    string value = field.Value;
                    if (int.TryParse(value, out int id))
                        value = field.Key switch
                        {
                            "Species" => Name(GameInfo.Strings.Species, id),
                            "HeldItem" or "AdditionalItem" => Name(GameInfo.Strings.Item, id),
                            "Nature" => Name(GameInfo.Strings.Natures, id),
                            "Goods" => Name(GameInfo.Strings.uggoods, id),
                            "PoketchApp" => Name(GameInfo.Strings.poketchapps, id),
                            "Course" => Name(GameInfo.Strings.walkercourses, id),
                            "Seal" => Name(GameInfo.Strings.seals, id),
                            "Accessory" => Name(GameInfo.Strings.accessories, id),
                            "Backdrop" => Name(GameInfo.Strings.backdrops, id),
                            _ => value,
                        };
                    if (field.Key == "Moves")
                        value = string.Join(", ", value.Split(',').Select(v => int.TryParse(v, out int move) ? Name(GameInfo.Strings.Move, move) : v));
                    rows.Add(new(loc["GiftPreview_" + field.Key], value));
                }
                void Metadata(string key, bool? value)
                {
                    if (value.HasValue) rows.Add(new(loc["GiftPreview_" + key], loc[value.Value ? "GiftPreview_Yes" : "GiftPreview_No"]));
                }
                Metadata("Collected", preview.Collected);
                Metadata("Repeatable", preview.Repeatable);
                Metadata("OncePerDay", preview.OncePerDay);
                PreviewRows = rows;
                if (_sprites is not null)
                {
                    try
                    {
                        if (gift.IsEntity)
                        {
                            if (gift.IsEgg)
                            {
                                var egg = EntityBlank.GetBlank(gift.Context);
                                egg.Species = gift.Species;
                                egg.Form = gift.Form;
                                Sprite = _sprites.GetSprite(egg, isEgg: true);
                            }
                            else Sprite = _sprites.GetSprite(gift.Species, gift.Form, gift.Gender, 0, gift.IsShiny, gift.Context);
                        }
                        else if (preview.Items.Count > 0)
                            Sprite = _sprites.GetItemSprite(preview.Items[0].Id, gift.Context, gift.Version);
                    }
                    catch (ArgumentOutOfRangeException) { }
                    catch (IndexOutOfRangeException) { }
                }
            }
            catch (ArgumentOutOfRangeException) { PreviewRows = [new(loc["GiftPreview_InvalidCard"], string.Empty)]; }
            catch (IndexOutOfRangeException) { PreviewRows = [new(loc["GiftPreview_InvalidCard"], string.Empty)]; }
        }
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(CardIdentity));
        OnPropertyChanged(nameof(PreviewRows));
    }

    private static string Name(IReadOnlyList<string> names, int id) => (uint)id < names.Count ? names[id] : $"#{id}";
}
