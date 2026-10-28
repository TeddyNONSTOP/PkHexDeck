using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class Gen7TrainerMapFlagTests
{
    [Theory]
    [InlineData(GameVersion.SN, 40, 44, 3200, 144, 176)]
    [InlineData(GameVersion.MN, 40, 44, 3200, 142, 178)]
    [InlineData(GameVersion.US, 46, 48, 4160, 144, 176)]
    [InlineData(GameVersion.UM, 46, 48, 4160, 142, 178)]
    public void NavigationBanksAreDistinctVersionAwareAndReadOnly(GameVersion version, int flyCount, int mapCount, int start, int lake, int altar)
    {
        var save = Assert.IsAssignableFrom<SAV7>(BlankSaveFile.Get(version)); var before = save.Data.ToArray();
        var fly = Gen7TrainerMapFlags.GetFlyDestinations(save); var map = Gen7TrainerMapFlags.GetMapUnmask(save);
        Assert.Equal(flyCount, fly.Count); Assert.Equal(mapCount, map.Count);
        Assert.Equal(start + 44, fly[0].FlagIndex); Assert.Equal(start + 5, map[0].FlagIndex);
        Assert.Equal(lake, fly[28].LocationId); Assert.Equal(altar, fly[36].LocationId);
        Assert.Equal(Gen7MapAlternateName.Home, fly[0].AlternateName);
        Assert.Equal(Gen7MapAlternateName.MelemeleSeaEast, map[3].AlternateName);
        Assert.Empty(fly.Select(f => f.FlagIndex).Intersect(map.Select(f => f.FlagIndex)));
        foreach (var item in fly.Concat(map)) _ = save.EventWork.GetEventFlag(item.FlagIndex);
        Assert.Equal(before, save.Data.ToArray());
    }
}
