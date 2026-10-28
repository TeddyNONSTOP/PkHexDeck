using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class Gen3TicketDefinitionTests
{
    [Theory]
    [InlineData(GameVersion.R, 1, 0x853)]
    [InlineData(GameVersion.S, 1, 0x853)]
    [InlineData(GameVersion.E, 4, 0x8B3)]
    [InlineData(GameVersion.FR, 2, 0x84A)]
    [InlineData(GameVersion.LG, 2, 0x84A)]
    public void CatalogMatchesGameAndPublicCoreKeyItemStorage(GameVersion version, int count, int firstGate)
    {
        var save = Assert.IsAssignableFrom<SAV3>(BlankSaveFile.Get(version));
        var definitions = Gen3TicketDefinitions.For(save);
        Assert.Equal(count, definitions.Count); Assert.Equal(firstGate, definitions[0].TravelFlag);
        var legal = save.Inventory.Info.GetItems(InventoryType.KeyItems).ToArray();
        foreach (var definition in definitions)
        {
            Assert.Contains((ushort)definition.ItemId, legal);
            Assert.InRange(definition.TravelFlag, 0, save.EventFlagCount - 1);
            if (definition.ShownFlag is { } shown) Assert.InRange(shown, 0, save.EventFlagCount - 1);
            if (definition.ReceivedFlag is { } received) Assert.InRange(received, 0, save.EventFlagCount - 1);
        }
    }
}
