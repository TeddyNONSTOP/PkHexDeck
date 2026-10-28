using PKHeX.Core;

namespace PKHeX.Application.Services;

public enum Dex9aMegaKind { Regular, X, Y, Z, MeowsticMale, MeowsticFemale, MagearnaNormal, MagearnaOriginal, TatsugiriCurly, TatsugiriDroopy, TatsugiriStretchy }
public readonly record struct Dex9aMegaFlag(byte Index, Dex9aMegaKind Kind);
public enum Dex9aBulkAction { SeenNone, SeenAll, CaughtNone, CaughtAll, Complete }

public static class Pokedex9aCapabilities
{
    public static IReadOnlyList<LanguageID> Languages { get; } = [LanguageID.Japanese, LanguageID.English, LanguageID.French, LanguageID.Italian, LanguageID.German, LanguageID.Spanish, LanguageID.Korean, LanguageID.ChineseS, LanguageID.ChineseT, LanguageID.SpanishL];

    public static ushort GetDexIndex(ushort species)
    {
        int count = PersonalTable.ZA[species].FormCount;
        for (byte form = 0; form < count; form++)
            if (PersonalTable.ZA[species, form].DexIndex is > 0 and var dex) return dex;
        return 0;
    }

    public static Dex9aMegaFlag[] GetMegaFlags(ushort species, int revision)
    {
        if (Zukan9a.IsMegaFormXY(species, revision)) return [new(0, Dex9aMegaKind.X), new(1, Dex9aMegaKind.Y)];
        if (Zukan9a.IsMegaFormZA(species, revision)) return [new(0, Dex9aMegaKind.Regular), new(1, Dex9aMegaKind.Z)];
        return species switch
        {
            (ushort)Species.Meowstic => [new(0, Dex9aMegaKind.MeowsticMale), new(1, Dex9aMegaKind.MeowsticFemale)],
            (ushort)Species.Magearna => [new(0, Dex9aMegaKind.MagearnaNormal), new(1, Dex9aMegaKind.MagearnaOriginal)],
            (ushort)Species.Tatsugiri => [new(0, Dex9aMegaKind.TatsugiriCurly), new(1, Dex9aMegaKind.TatsugiriDroopy), new(2, Dex9aMegaKind.TatsugiriStretchy)],
            _ when FormInfo.HasMegaForm(species) => [new(0, Dex9aMegaKind.Regular)],
            _ => [],
        };
    }

    public static uint GetFormMask(ushort species)
    {
        var strings = GameInfo.Strings;
        int names = FormConverter.GetFormList(species, strings.Types, strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9a).Length;
        int count = Math.Clamp(Math.Max((int)PersonalTable.ZA[species].FormCount, names), 1, 32);
        uint mask = count == 32 ? uint.MaxValue : (1u << count) - 1;
        if (Zukan9a.GetFormExtraFlags(species, out var extra)) mask |= extra;
        return mask;
    }
}
