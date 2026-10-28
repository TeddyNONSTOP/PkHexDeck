using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Why a Bank entity could not be converted into a save's native format.</summary>
public enum BankConversionFailure
{
    None = 0,
    /// <summary><see cref="EntityConverter"/> has no route from the stored format to the save's format.</summary>
    NoConversionRoute,
    /// <summary>A Gen 1/2 entity's Japanese/international language set does not match the save's.</summary>
    IncompatibleLanguage,
    /// <summary>The converted entity uses species/moves/items/forms the save's game does not support.</summary>
    IncompatibleWithSave,
    /// <summary>The conversion threw.</summary>
    ConversionError,
}

/// <summary>
/// Outcome of converting a Bank entity for a target save. <see cref="Details"/> carries PKHeX.Core's
/// own explanation lines (conversion messages / save errata) so the caller can show <i>why</i>.
/// </summary>
public sealed record BankConversionResult(PKM? Entity, BankConversionFailure Failure, IReadOnlyList<string> Details)
{
    public bool Success => Entity is not null && Failure == BankConversionFailure.None;

    public static BankConversionResult Ok(PKM pk) => new(pk, BankConversionFailure.None, []);
    public static BankConversionResult Fail(BankConversionFailure failure, IReadOnlyList<string> details) => new(null, failure, details);
}

/// <summary>
/// Converts a stored Bank entity into the target save's native format at the moment of withdrawal,
/// using exactly the checks <see cref="SaveExtensions.GetCompatible"/> applies for folder imports and
/// the cross-window/OS drop path (<see cref="UseCases.ImportEntityFileUseCase"/>) —
/// <see cref="EntityConverter.ConvertToType"/>, the Gen 1/2 language-set check, then
/// <see cref="SaveExtensions.EvaluateCompatibility"/> — but reporting the reason instead of silently
/// dropping an incompatible entity. Honors the user's global
/// <see cref="EntityConverter.AllowIncompatibleConversion"/> setting like every other conversion.
/// </summary>
public static class BankEntityConversion
{
    public static BankConversionResult ConvertForSave(SaveFile sav, PKM pk)
    {
        ArgumentNullException.ThrowIfNull(sav);
        ArgumentNullException.ThrowIfNull(pk);

        try
        {
            var destination = sav.PKMType;
            var converted = EntityConverter.ConvertToType(pk.Clone(), destination, out var conversion);
            if (converted is null)
                return BankConversionResult.Fail(BankConversionFailure.NoConversionRoute, [conversion.GetDisplayString(pk, destination)]);

            if (sav is ILangDeviantSave il && !EntityConverter.IsCompatibleGB(pk, il.Japanese, converted.Japanese))
            {
                var message = EntityConverterResult.IncompatibleLanguageGB.GetIncompatibleGBMessage(converted, il.Japanese);
                return BankConversionResult.Fail(BankConversionFailure.IncompatibleLanguage, [message]);
            }

            var errata = sav.EvaluateCompatibility(converted, GameInfo.CurrentLanguage);
            if (errata.Count > 0)
                return BankConversionResult.Fail(BankConversionFailure.IncompatibleWithSave, errata);

            return BankConversionResult.Ok(converted);
        }
        catch (Exception ex)
        {
            return BankConversionResult.Fail(BankConversionFailure.ConversionError, [ex.Message]);
        }
    }
}
