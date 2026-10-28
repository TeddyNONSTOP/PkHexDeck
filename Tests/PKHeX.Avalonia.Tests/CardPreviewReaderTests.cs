using PKHeX.Application.UseCases;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class CardPreviewReaderTests
{
    [Fact]
    public void UnknownGiftKindUsesReadableFallbackWithoutChangingBytes()
    {
        var gift = new WC8 { CardType = (WC8.GiftType)255, ItemID = 23 };
        var before = gift.Data.ToArray();
        var preview = new ReadCardPreviewUseCase().Execute(gift);
        Assert.Equal("Other", preview.Kind);
        Assert.Equal(before, gift.Data.ToArray());
    }

    [Fact]
    public void NonPokemonGiftKindsExposeStoredPayloadWithoutMutation()
    {
        DataMysteryGift[] gifts =
        [
            new WC7 { IsBP = true }, new WC7 { IsBean = true }, new PGF { IsPower = true },
            new WC8 { CardType = WC8.GiftType.Clothing }, new WB8 { CardType = WB8.GiftType.Money },
            new WB8 { CardType = WB8.GiftType.UnderGroundItem },
        ];
        foreach (var gift in gifts)
        {
            gift.ItemID = 23;
            gift.Quantity = 11;
            var before = gift.Data.ToArray();
            var preview = new ReadCardPreviewUseCase().Execute(gift);
            Assert.NotEqual("Other", preview.Kind);
            Assert.Contains(preview.Fields, field => field.Key == "PayloadId" && field.Value == "23");
            Assert.Contains(preview.Fields, field => field.Key == "Quantity");
            Assert.Equal(before, gift.Data.ToArray());
        }
    }

    [Fact]
    public void LgpeRecordShowsStoredItemQuantitiesWithoutInventedMetadata()
    {
        var gift = new WR7 { IsItem = true, ItemCount = 2, ItemID = 100, ItemIDCount = 5,
            ItemSet2Item = 101, ItemSet2Count = 7, Epoch = 86400 };
        var before = gift.Data.ToArray();
        var preview = new ReadCardPreviewUseCase().Execute(gift);
        Assert.Equal([new CardPreviewItem(100, 5), new CardPreviewItem(101, 7)], preview.Items);
        Assert.Null(preview.Collected);
        Assert.Null(preview.Repeatable);
        Assert.Null(preview.OncePerDay);
        Assert.Contains(preview.Fields, field => field.Key == "ReceivedEpoch" && field.Value == "86400");
        Assert.Equal(before, gift.Data.ToArray());
    }

    [Fact]
    public void BdspSeventhItemAndGapsArePreservedInPreview()
    {
        var gift = new WB8 { IsItem = true };
        gift.SetItem(0, 100);
        gift.SetQuantity(0, 2);
        gift.SetItem(6, 101);
        gift.SetQuantity(6, 3);
        var preview = new ReadCardPreviewUseCase().Execute(gift);
        Assert.Equal([new CardPreviewItem(100, 2), new CardPreviewItem(101, 3)], preview.Items);
    }

    [Fact]
    public void MultiItemCardIncludesBothQuantitiesAndMetadataWithoutMutation()
    {
        var gift = new WC7 { IsItem = true, CardID = 123, GiftRepeatable = true, GiftUsed = true, GiftOncePerDay = true };
        gift.SetItem(0, 100);
        gift.SetQuantity(0, 2);
        gift.SetItem(1, 101);
        gift.SetQuantity(1, 3);
        var bytes = gift.Data.ToArray();
        var preview = new ReadCardPreviewUseCase().Execute(gift);
        Assert.Equal([new CardPreviewItem(100, 2), new CardPreviewItem(101, 3)], preview.Items);
        Assert.True(preview.Collected);
        Assert.True(preview.Repeatable);
        Assert.True(preview.OncePerDay);
        Assert.Equal(bytes, gift.Data.ToArray());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(70)]
    [InlineData(8)]
    [InlineData(80)]
    [InlineData(81)]
    [InlineData(9)]
    [InlineData(90)]
    public void PokemonTemplatesAreReadOnlyAcrossFormats(int format)
    {
        DataMysteryGift gift = format switch
        {
            5 => new PGF(), 6 => new WC6(), 7 => new WC7(), 70 => new WB7(),
            8 => new WC8(), 80 => new WA8(), 81 => new WB8(), 9 => new WC9(), _ => new WA9(),
        };
        gift.IsEntity = true;
        gift.Species = 25;
        gift.HeldItem = 100;
        var bytes = gift.Data.ToArray();
        var preview = new ReadCardPreviewUseCase().Execute(gift);
        Assert.Equal("Pokemon", preview.Kind);
        Assert.Contains(preview.Fields, field => field.Key == "Species" && field.Value == "25");
        Assert.Contains(preview.Fields, field => field.Key == "HeldItem" && field.Value == "100");
        Assert.Equal(bytes, gift.Data.ToArray());
    }
}
