using System;
using static PKHeX.Core.Species;

namespace PKHeX.Core;

/// <summary>
/// Species and form availability for Luminescent Platinum 2.0.1F, ported from PKLumiHeX (TalonSabre/PKLumiHex).
/// </summary>
/// <remarks>
/// Luminescent Platinum has no learnset, encounter, or TM data in Core, so availability is the only legality check performed.
/// </remarks>
public static class FormInfoLumi
{
    /// <summary>
    /// Checks if the <paramref name="species"/> and <paramref name="form"/> cannot be obtained in Luminescent Platinum.
    /// </summary>
    /// <remarks>Unavailable species and forms appear as Ditto in-game.</remarks>
    public static bool IsUnavailable(ushort species, byte form)
    {
        if (GetUnavailableForms(species).Contains(form))
            return true;
        if (IsNewGenAvailable(species))
            return false;
        return species > (ushort)Arceus;
    }

    /// <summary>
    /// Forms of species introduced before Gen 5 that cannot be found in Luminescent Platinum.
    /// </summary>
    private static ReadOnlySpan<byte> GetUnavailableForms(ushort species) => (Species)species switch
    {
        Butterfree or Beedrill or Pidgeot or Alakazam or Machamp or Slowbro
        or Dewgong or Kingler or Kangaskhan or Pinsir or Gyarados or Lapras
        or Aerodactyl or Snorlax
        or Ampharos or Wooper or Steelix or Scizor or Heracross or Houndoom or Tyranitar
        or Sceptile or Blaziken or Swampert or Gardevoir or Sableye or Mawile
        or Aggron or Medicham or Manectric or Sharpedo or Camerupt or Altaria
        or Banette or Absol or Glalie or Salamence or Metagross
        or Latias or Latios or Kyogre or Groudon or Rayquaza
        or Lopunny or Garchomp or Lucario or Abomasnow or Gallade
        or Dialga or Palkia => [1],

        Raticate or Marowak or Eevee => [2],

        Meowth => [3],

        Venusaur or Blastoise or Gengar or Mewtwo => [1, 2],

        Charizard or Tauros => [1, 2, 3],

        Pikachu => [7, 8, 9, 10, 11, 12, 13, 14, 15, 16],

        _ => [],
    };

    /// <summary>
    /// Species introduced after Gen 4 that can be found in Luminescent Platinum.
    /// </summary>
    private static bool IsNewGenAvailable(ushort species) => (Species)species is
        Litwick or Lampent or Chandelure
        or Sylveon
        or Pikipek or Trumbeak or Toucannon
        or Perrserker or Sirfetchd or MrRime or Cursola or Obstagoon
        or Kleavor or Overqwil or Sneasler or Wyrdeer or Ursaluna
        or Tinkatink or Tinkatuff or Tinkaton;
}
