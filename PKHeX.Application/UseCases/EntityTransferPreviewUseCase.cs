using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>A property compared by <see cref="EntityTransferPreviewUseCase"/>.</summary>
public enum EntityTransferField
{
    Format,
    Species,
    Nickname,
    Egg,
    Shiny,
    Gender,
    Level,
    Nature,
    Ability,
    HeldItem,
    Ball,
    Move1,
    Move2,
    Move3,
    Move4,
    OriginalTrainer,
    TrainerId,
    Language,
    MetLocation,
    IVs,
    EVs,
}

/// <summary>
/// One property that differs between the Pokémon before and after the transfer. <see cref="Before"/> and
/// <see cref="After"/> are display text from PKHeX.Core's game data, except for the yes/no
/// <see cref="EntityTransferField.Egg"/> and <see cref="EntityTransferField.Shiny"/> fields, which carry
/// <see cref="bool.TrueString"/>/<see cref="bool.FalseString"/> for the caller to localize.
/// </summary>
public sealed record EntityTransferChange(EntityTransferField Field, string Before, string After)
{
    public bool IsFlag => Field is EntityTransferField.Egg or EntityTransferField.Shiny;
}

/// <summary>Result of <see cref="EntityTransferPreviewUseCase.Build"/>.</summary>
/// <param name="Source">The Pokémon as it is now (Bank entry, dropped file or other save).</param>
/// <param name="Result">What would be stored in the target save, including the save's own trade adaptation.</param>
/// <param name="Changes">Properties that differ, in a fixed reading order.</param>
/// <param name="SourceLegal">Legality of <paramref name="Source"/> in its own format.</param>
/// <param name="ResultLegal">Legality of <paramref name="Result"/> in the target save.</param>
/// <param name="LegalityReport">PKHeX.Core's legality report for <paramref name="Result"/>.</param>
public sealed record EntityTransferPreview(
    PKM Source,
    PKM Result,
    IReadOnlyList<EntityTransferChange> Changes,
    bool SourceLegal,
    bool ResultLegal,
    string LegalityReport)
{
    public bool FormatChanged => Source.GetType() != Result.GetType();

    /// <summary>The transfer makes a legal Pokémon illegal.</summary>
    public bool LosesLegality => SourceLegal && !ResultLegal;

    /// <summary>
    /// Something worth showing before writing: a format conversion, a changed property, or lost legality.
    /// A same-format transfer that the save takes as it is needs no preview.
    /// </summary>
    public bool IsNotable => FormatChanged || Changes.Count > 0 || LosesLegality;
}

/// <summary>
/// Read-only preview of a transfer into a save: compares a Pokémon with its converted form, as the
/// save would store it, and checks the legality of both. Nothing is written: the converted entity is
/// cloned before the save's trade adaptation (<see cref="SaveFile.AdaptToSaveFile"/>) is applied.
/// </summary>
public sealed class EntityTransferPreviewUseCase
{
    public EntityTransferPreview Build(SaveFile target, PKM source, PKM converted)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(converted);

        var result = converted.Clone();
        target.AdaptToSaveFile(result, isParty: false);

        // Show both IDs the way the target displays them, so an unchanged ID never reads as a change.
        var idFormat = result.GetTrainerIDFormat();
        var before = Describe(source, idFormat);
        var after = Describe(result, idFormat);
        var changes = new List<EntityTransferChange>();
        foreach (var field in Enum.GetValues<EntityTransferField>())
        {
            var a = before[field];
            var b = after[field];
            if (!string.Equals(a, b, StringComparison.Ordinal))
                changes.Add(new EntityTransferChange(field, a, b));
        }

        var sourceLegal = IsLegal(source, null, out _);
        var resultLegal = IsLegal(result, target.Personal, out var report);
        return new EntityTransferPreview(source, result, changes, sourceLegal, resultLegal, report);
    }

    private static bool IsLegal(PKM pk, IPersonalTable? personal, out string report)
    {
        try
        {
            var la = personal is null ? new LegalityAnalysis(pk) : new LegalityAnalysis(pk, personal);
            report = la.Report();
            return la.Valid;
        }
        catch (Exception ex)
        {
            report = ex.Message;
            return false;
        }
    }

    private static Dictionary<EntityTransferField, string> Describe(PKM pk, TrainerIDFormat idFormat)
    {
        var strings = GameInfo.Strings;
        var moves = new ushort[4];
        pk.GetMoves(moves);
        var ivs = new int[6];
        pk.GetIVs(ivs);
        var evs = new int[6];
        pk.GetEVs(evs);

        return new Dictionary<EntityTransferField, string>
        {
            [EntityTransferField.Format] = pk.GetType().Name,
            [EntityTransferField.Species] = SpeciesName(pk, strings),
            [EntityTransferField.Nickname] = pk.IsNicknamed ? pk.Nickname : string.Empty,
            [EntityTransferField.Egg] = pk.IsEgg.ToString(),
            [EntityTransferField.Shiny] = pk.IsShiny.ToString(),
            [EntityTransferField.Gender] = pk.Gender < GameInfo.GenderSymbolUnicode.Count ? GameInfo.GenderSymbolUnicode[pk.Gender] : pk.Gender.ToString(),
            [EntityTransferField.Level] = pk.CurrentLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [EntityTransferField.Nature] = Name(strings.natures, (int)pk.Nature),
            [EntityTransferField.Ability] = pk.Format < 3 ? string.Empty : Name(strings.abilitylist, pk.Ability),
            [EntityTransferField.HeldItem] = pk.HeldItem == 0 ? string.Empty : Name(strings.GetItemStrings(pk.Context, pk.Version), pk.HeldItem),
            [EntityTransferField.Ball] = Name(strings.balllist, pk.Ball),
            [EntityTransferField.Move1] = MoveName(strings, moves[0]),
            [EntityTransferField.Move2] = MoveName(strings, moves[1]),
            [EntityTransferField.Move3] = MoveName(strings, moves[2]),
            [EntityTransferField.Move4] = MoveName(strings, moves[3]),
            [EntityTransferField.OriginalTrainer] = pk.OriginalTrainerName,
            [EntityTransferField.TrainerId] = TrainerId(pk, idFormat),
            [EntityTransferField.Language] = pk.Format < 3 && pk.Language == 0 ? string.Empty : ((LanguageID)pk.Language).ToString(),
            [EntityTransferField.MetLocation] = MetLocation(pk),
            [EntityTransferField.IVs] = string.Join("/", ivs),
            [EntityTransferField.EVs] = string.Join("/", evs),
        };
    }

    private static string SpeciesName(PKM pk, GameStrings strings)
    {
        var species = Name(strings.specieslist, pk.Species);
        var form = FormConverter.GetStringFromForm(pk.Species, pk.Form, strings, GameInfo.GenderSymbolASCII, pk.Context);
        return string.IsNullOrWhiteSpace(form) ? species : $"{species} ({form})";
    }

    private static string MoveName(GameStrings strings, ushort move) => move == 0 ? string.Empty : Name(strings.movelist, move);

    private static string TrainerId(PKM pk, TrainerIDFormat format) => format switch
    {
        TrainerIDFormat.SixDigit => $"{pk.ID32 % 1_000_000:000000}-{pk.ID32 / 1_000_000:0000}",
        TrainerIDFormat.SixteenBitSingle => $"{pk.TID16:00000}",
        _ => $"{pk.TID16:00000}-{pk.SID16:00000}",
    };

    private static string MetLocation(PKM pk)
    {
        if (pk.Format < 2 || (pk.Format == 2 && pk.MetLocation == 0))
            return string.Empty;
        try
        {
            // A Gen 3 Pokémon in Gen 4 format has its met location (Pal Park) in the Gen 4 table.
            var generation = pk is { Format: 4, Generation: 3 } ? (byte)4 : pk.Generation;
            var name = GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, generation, pk.Version);
            return string.IsNullOrWhiteSpace(name) ? pk.MetLocation.ToString(System.Globalization.CultureInfo.InvariantCulture) : name;
        }
        catch (ArgumentException)
        {
            return pk.MetLocation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string Name(IReadOnlyList<string> list, int index)
        => (uint)index < (uint)list.Count && !string.IsNullOrWhiteSpace(list[index])
            ? list[index]
            : index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
