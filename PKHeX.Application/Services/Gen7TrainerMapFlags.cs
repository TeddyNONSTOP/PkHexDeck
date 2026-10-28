using PKHeX.Core;

namespace PKHeX.Application.Services;

public enum Gen7MapAlternateName { None, Home, HauoliPhotoClub, KonikoniPhotoClub, MelemeleSeaEast, MelemeleSeaWest }
public sealed record Gen7TrainerMapFlag(int FlagIndex, int LocationId, Gen7MapAlternateName AlternateName);

/// <summary>Verified SM/USUM navigation flags from the upstream trainer editor; never mutates a save.</summary>
public static class Gen7TrainerMapFlags
{
    private static readonly int[] FlyLocations =
    [ -1,24,34,8,20,38,12,46,40,30,70,68,78,86,74,104,82,58,90,72,76,92,62,
      132,136,138,114,118,144,130,154,140,172,184,180,174,176,156,186,188,-1,-1,198,202,110,204 ];
    private static readonly int[] FlyOffsets =
    [ 44,43,45,40,41,49,42,47,46,48,50,54,39,57,51,55,59,52,58,53,61,60,56,
      62,66,67,64,65,273,270,37,38,69,74,72,71,276,73,70,75,332,334,331,333,335,336 ];
    private static readonly int[] UnmaskLocations =
    [ 6,8,24,-1,18,-1,20,22,12,10,14,70,50,68,52,74,54,56,58,60,72,62,64,
      132,192,106,108,122,112,114,126,116,118,120,154,172,158,160,162,164,166,168,170,188,198,202,110,204 ];
    private static readonly int[] UnmaskOffsets =
    [ 5,76,82,91,79,84,80,81,77,78,83,19,10,18,11,21,12,13,14,15,20,16,17,
      33,34,30,31,98,92,93,94,95,96,97,141,173,144,145,146,147,148,149,172,181,409,297,32,296 ];

    public static IReadOnlyList<Gen7TrainerMapFlag> GetFlyDestinations(SAV7 save)
    {
        int count = FlyLocations.Length - (save is SAV7USUM ? 0 : 6), start = save is SAV7USUM ? 4160 : 3200;
        var result = new Gen7TrainerMapFlag[count];
        for (int i = 0; i < count; i++)
        {
            int location = FlyLocations[i];
            if (save.Version is GameVersion.MN or GameVersion.UM)
            { if (i == 28) location = 142; if (i == 36) location = 178; }
            var alternate = i switch { 0 => Gen7MapAlternateName.Home, 40 => Gen7MapAlternateName.HauoliPhotoClub,
                41 => Gen7MapAlternateName.KonikoniPhotoClub, _ => Gen7MapAlternateName.None };
            result[i] = new(start + FlyOffsets[i], location, alternate);
        }
        return result;
    }

    public static IReadOnlyList<Gen7TrainerMapFlag> GetMapUnmask(SAV7 save)
    {
        int count = UnmaskLocations.Length - (save is SAV7USUM ? 0 : 4), start = save is SAV7USUM ? 4160 : 3200;
        return Enumerable.Range(0, count).Select(i => new Gen7TrainerMapFlag(start + UnmaskOffsets[i], UnmaskLocations[i],
            i switch { 3 => Gen7MapAlternateName.MelemeleSeaEast, 5 => Gen7MapAlternateName.MelemeleSeaWest, _ => Gen7MapAlternateName.None })).ToArray();
    }
}
