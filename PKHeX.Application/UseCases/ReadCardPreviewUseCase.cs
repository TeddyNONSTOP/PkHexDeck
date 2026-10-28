using System.Globalization;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

public sealed record CardPreviewField(string Key, string Value);
public sealed record CardPreviewItem(int Id, int Quantity);
public sealed record CardPreview(int Id, string Title, string Format, string Kind,
    bool? Collected, bool? Repeatable, bool? OncePerDay,
    IReadOnlyList<CardPreviewItem> Items, IReadOnlyList<CardPreviewField> Fields);

/// <summary>Reads the stored template without generating a Pokémon or normalizing card bytes.</summary>
public sealed class ReadCardPreviewUseCase
{
    public CardPreview Execute(DataMysteryGift gift)
    {
        var items = new List<CardPreviewItem>();
        var fields = new List<CardPreviewField>();
        void Add(string key, object value) => fields.Add(new(key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
        if (gift is WR7 record)
        {
            Add("ReceivedEpoch", record.Epoch);
            Add("Language", (int)record.LanguageReceived);
            if (record.IsEntity)
            {
                Add("Species", record.Species);
                Add("LevelRaw", record.Level);
                Add("OriginalTrainer", record.OriginalTrainerName);
            }
            if (record.IsItem)
            {
                var entries = new[]
                {
                    new CardPreviewItem(record.ItemID, record.ItemIDCount),
                    new CardPreviewItem(record.ItemSet2Item, record.ItemSet2Count),
                    new CardPreviewItem(record.ItemSet3Item, record.ItemSet3Count),
                    new CardPreviewItem(record.ItemSet4Item, record.ItemSet4Count),
                    new CardPreviewItem(record.ItemSet5Item, record.ItemSet5Count),
                    new CardPreviewItem(record.ItemSet6Item, record.ItemSet6Count),
                };
                items.AddRange(entries.Take(Math.Min(record.ItemCount, entries.Length)).Where(item => item.Id != 0));
            }
            return new(record.CardID, record.CardTitle, record.Type,
                record.IsEntity ? "Pokemon" : record.IsItem ? "Item" : "Other", null, null, null, items, fields);
        }
        string kind = gift.IsEntity ? (gift.IsEgg ? "Egg" : "Pokemon") : gift.IsItem ? "Item" : "Other";
        if (gift.IsEntity)
        {
            Add("Species", gift.Species);
            Add("Form", gift.Form);
            Add("Level", gift.Level);
            Add("Gender", gift.Gender);
            Add("Ball", gift.Ball);
            Add("HeldItem", gift.HeldItem);
            Add("OriginalTrainer", gift.OriginalTrainerName);
            Add("TrainerId", gift.DisplayTID);
            Add("SecretId", gift.DisplaySID);
            Add("Location", gift.Location);
            Add("EggLocation", gift.EggLocation);
            var moves = gift.Moves;
            Add("Moves", string.Join(",", new[] { moves.Move1, moves.Move2, moves.Move3, moves.Move4 }));
            Add("Ability", gift.Ability);
            try { Add("Shiny", gift.Shiny); }
            catch (ArgumentOutOfRangeException) { Add("Shiny", string.Empty); }
            if (gift is INature nature) Add("Nature", (int)nature.Nature);
            int[] ivs = new int[6];
            gift.GetIVs(ivs);
            Add("IVs", string.Join(" / ", ivs));
            if (gift is PGF pgf) { Add("Language", pgf.Language); Add("Nickname", pgf.Nickname); }
            if (gift is WC6 wc6) { Add("Language", wc6.Language); Add("Nickname", wc6.Nickname); }
            if (gift is WC7 wc7) { Add("Language", wc7.Language); Add("Nickname", wc7.Nickname); }
            if (gift is WB7 or WC8 or WB8 or WA8 or WC9 or WA9)
                foreach (int language in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10 })
                {
                    var variant = gift switch
                    {
                        WB7 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        WC8 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        WB8 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        WA8 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        WC9 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        WA9 x => (x.GetLanguage(language), x.GetOT(language), x.GetNickname(language)),
                        _ => (0, string.Empty, string.Empty),
                    };
                    Add("LanguageVariant", $"{language} → {variant.Item1}: {variant.Item2} / {variant.Item3}");
                }
            if (gift is WC7 w7 && w7.AdditionalItem != 0) Add("AdditionalItem", w7.AdditionalItem);
        }
        else if (gift.IsItem)
        {
            int count = gift switch { WC7 => (gift.Data.Length - 0x68) / 4, WB8 => 7, WB7 or WC8 or WA8 or WC9 or WA9 => 6, _ => 1 };
            for (int i = 0; i < count; i++)
            {
                int id = gift switch { WC7 x => x.GetItem(i), WB7 x => x.GetItem(i), WC8 x => x.GetItem(i), WB8 x => x.GetItem(i), WA8 x => x.GetItem(i), WC9 x => x.GetItem(i), WA9 x => x.GetItem(i), _ => gift.ItemID };
                int quantity = gift switch { WC7 x => x.GetQuantity(i), WB7 x => x.GetQuantity(i), WC8 x => x.GetQuantity(i), WB8 x => x.GetQuantity(i), WA8 x => x.GetQuantity(i), WC9 x => x.GetQuantity(i), WA9 x => x.GetQuantity(i), _ => gift.Quantity };
                if (id == 0)
                {
                    if (gift is WC7) break;
                    continue;
                }
                items.Add(new(id, quantity));
            }
        }
        else
        {
            kind = gift switch
            {
                WC7 { IsBP: true } => "BattlePoints", WC7 { IsBean: true } => "Beans",
                PGF { IsPower: true } => "Power",
                WC8 x => Kind(x.CardType), WB8 x => Kind(x.CardType),
                WA8 x => Kind(x.CardType), WC9 x => Kind(x.CardType), WA9 x => Kind(x.CardType),
                _ => "Other",
            };
            Add("PayloadId", gift.ItemID);
            Add("Quantity", gift.Quantity);
            var raw = gift switch { PGT x => x, PCD x => x.Gift, _ => null };
            if (raw is not null)
            {
                Add("OtherType", (int)raw.GiftType);
                switch (raw.GiftType)
                {
                    case GiftType4.Goods: Add("Goods", raw.ItemID); break;
                    case GiftType4.PokétchApp: Add("PoketchApp", (int)raw.PoketchApp); break;
                    case GiftType4.PokéwalkerCourse: Add("Course", raw.PokewalkerCourseID); break;
                    case GiftType4.HasSubType:
                        switch (raw.GiftSubType)
                        {
                            case GiftSubType4.Seal: Add("Seal", (int)raw.Seal); break;
                            case GiftSubType4.Accessory: Add("Accessory", (int)raw.Accessory); break;
                            case GiftSubType4.Backdrop: Add("Backdrop", (int)raw.Backdrop); break;
                            default: Add("Subtype", (int)raw.GiftSubType); Add("SubId", raw.ItemSubID); break;
                        }
                        break;
                }
            }
        }
        bool? collected = gift is PGF or WC6 or WC7 or WB7 ? gift.GiftUsed : null;
        bool? repeatable = gift switch
        {
            PGF x => x.MultiObtain, WC6 x => x.MultiObtain, WC7 x => x.GiftRepeatable,
            WB7 x => x.GiftRepeatable, WC8 x => x.GiftRepeatable, WB8 x => x.GiftRepeatable,
            WA8 x => x.GiftRepeatable, WC9 x => x.GiftRepeatable, WA9 x => x.GiftRepeatable, _ => null,
        };
        bool? daily = gift switch { WC7 x => x.GiftOncePerDay, WB7 x => x.GiftOncePerDay, WB8 x => x.GiftOncePerDay, _ => null };
        return new(gift.CardID, gift.CardTitle, gift.Type, kind, collected, repeatable, daily, items, fields);
    }
    private static string Kind<T>(T value) where T : struct, Enum => Enum.IsDefined(value) ? value.ToString() : "Other";

}
