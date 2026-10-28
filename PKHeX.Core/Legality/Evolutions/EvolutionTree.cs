using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace PKHeX.Core;

/// <summary>
/// Generation specific Evolution Tree data.
/// </summary>
/// <remarks>
/// Used to determine if a <see cref="PKM.Species"/> can evolve from prior steps in its evolution branch.
/// </remarks>
public sealed class EvolutionTree : EvolutionNetwork
{
    public const int MaxEvolutions = 3;
    public static readonly EvolutionTree Evolves1  = GetViaSpecies (PersonalTable.Y,    Get("g1", "g1"u8));
    public static readonly EvolutionTree Evolves2  = GetViaSpecies (PersonalTable.C,    Get("g2", "g2"u8));
    public static readonly EvolutionTree Evolves3  = GetViaSpecies (PersonalTable.RS,   Get("g3", "g3"u8));
    public static readonly EvolutionTree Evolves4  = GetViaSpecies (PersonalTable.DP,   Get("g4", "g4"u8));
    public static readonly EvolutionTree Evolves5  = GetViaSpecies (PersonalTable.BW,   Get("g5", "g5"u8));
    public static readonly EvolutionTree Evolves6  = GetViaSpecies (PersonalTable.AO,   Get("g6", "g6"u8));
    public static readonly EvolutionTree Evolves7  = GetViaPersonal(PersonalTable.USUM, Get("uu", "uu"u8));
    public static readonly EvolutionTree Evolves7b = GetViaPersonal(PersonalTable.GG,   Get("gg", "gg"u8));
    public static readonly EvolutionTree Evolves8  = GetViaPersonal(PersonalTable.SWSH, Get("ss", "ss"u8));
    public static readonly EvolutionTree Evolves8a = GetViaPersonal(PersonalTable.LA,   Get("la", "la"u8, 0));
    public static readonly EvolutionTree Evolves8b = GetViaPersonal(PersonalTable.BDSP, Get("bs", "bs"u8));
    public static readonly EvolutionTree Evolves9  = GetViaPersonal(PersonalTable.SV,   Get("sv", "sv"u8));
    public static readonly EvolutionTree Evolves9a = GetViaPersonal(PersonalTable.ZA,   Get("za", "za"u8, 0));

    private static EvolutionMethod[][] Get([ConstantExpected] string resource, [Length(2, 2)] ReadOnlySpan<byte> identifier, [ConstantExpected] byte levelUp = 1)
    {
        var data = Util.GetBinaryResource($"evos_{resource}.pkl");
        var bla = BinLinkerAccessor16.Get(data, identifier);
        return EvolutionSet.GetArray(bla, levelUp);
    }

    private EvolutionTree(IEvolutionForward forward, IEvolutionReverse reverse) : base(forward, reverse) { }

    private static EvolutionTree GetViaSpecies(IPersonalTable t, EvolutionMethod[][] entries)
    {
        var forward = new EvolutionForwardSpecies(entries);
        var reverse = new EvolutionReverseSpecies(entries, t);
        return new EvolutionTree(forward, reverse);
    }

    private static EvolutionTree GetViaPersonal(IPersonalTable t, EvolutionMethod[][] entries)
    {
        var forward = new EvolutionForwardPersonal(entries, t);
        var reverse = new EvolutionReversePersonal(entries, t);
        return new EvolutionTree(forward, reverse);
    }

    /// <summary>
    /// Get the <see cref="EvolutionTree"/> for the given <see cref="EntityContext"/>.
    /// </summary>
    /// <param name="context">Context to get the <see cref="EvolutionTree"/> for.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static EvolutionTree GetEvolutionTree(EntityContext context) => context switch
    {
        EntityContext.Gen1 => Evolves1,
        EntityContext.Gen2 => Evolves2,
        EntityContext.Gen3 => Evolves3,
        EntityContext.Gen4 => Evolves4,
        EntityContext.Gen5 => Evolves5,
        EntityContext.Gen6 => Evolves6,
        EntityContext.Gen7 => Evolves7,
        EntityContext.Gen8 => Evolves8,
        EntityContext.Gen9 => Evolves9,
        EntityContext.Gen7b => Evolves7b,
        EntityContext.Gen8a => Evolves8a,
        EntityContext.Gen8b => Evolves8b,
        // KNOWN GAP (investigated Stage 2, not a quick-fix): reuses vanilla BDSP's tree, so evolution-chain
        // legality for species above the original Sinnoh dex (494+) isn't validated for Luminescent saves.
        //
        // Root cause: EvolutionReversePersonal.GetLineage(t, entries) indexes `entries[t.GetFormIndex(species,
        // form)]` - the entries array (decoded from an evos_*.pkl resource) must be indexed EXACTLY according to
        // the specific personal-table binary it was co-generated with by PKHeX's official data-generation
        // tooling. `personal_bdsplumi` is a third-party community resource (ported from the reference WinForms
        // fork in Stage 1) with no correspondingly-indexed evos-pkl of its own.
        //   - Pairing PersonalTable.BDSPLUMI with evos_bs.pkl (493-species-sized) throws IndexOutOfRangeException
        //     for any species above ~493 (verified directly).
        //   - Pairing it with evos_sv.pkl instead (correctly sized for the full modern species range, which
        //     would seem like the fix given Luminescent reuses vanilla evolution methods for vanilla species -
        //     see Stage 1's finding of no new species/moves) does NOT throw, but is not safe either: evos_sv.pkl's
        //     indices are keyed to personal_sv's own internal FormIndex ordering, not personal_bdsplumi's -
        //     nothing guarantees the two resources order species/forms identically, so this would silently
        //     misassign evolution data to the wrong species rather than fail loudly.
        // A real fix needs an evos-pkl regenerated specifically against personal_bdsplumi's own FormIndex
        // ordering, which requires PKHeX's official binary-resource-generation tooling (not available in this
        // session) - this is a source-data gap, not a code gap. Aliasing to Evolves8b is safe (no crash, correct
        // for the Sinnoh-dex subset) and deliberately left in place rather than risking silently-wrong data.
        EntityContext.Gen8bLumi => Evolves8b,
        EntityContext.Gen9a => Evolves9a,
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
    };
}
