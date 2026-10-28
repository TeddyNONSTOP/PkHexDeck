using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>
/// Builds the raw-bytes + display-metadata pair the Bank stores for a <see cref="PKM"/>, shared by
/// every deposit path (box/party "send to Bank", and Bank archive import).
/// </summary>
public static class BankEntryInfoFactory
{
    /// <summary>Exact decrypted native-format bytes, untouched — never normalized to a universal format.</summary>
    public static byte[] GetRawData(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }

    /// <summary>
    /// Lightweight display metadata captured at deposit time. <see cref="BankEntryInfo.Format"/> is
    /// <see cref="PKM.Extension"/> (e.g. <c>"pk9"</c>), which disambiguates same-size native structs
    /// (e.g. PK8 vs PB8) when the raw bytes are later reconstructed via
    /// <see cref="FileUtil.GetSupportedFile(System.Memory{byte}, System.ReadOnlySpan{char}, SaveFile?)"/>.
    /// </summary>
    public static BankEntryInfo CreateInfo(PKM pk, string sourceLabel) => new(
        Species: pk.Species,
        Form: pk.Form,
        IsShiny: pk.Species != 0 && pk.IsShiny,
        Nickname: pk.Nickname,
        Level: (byte)pk.CurrentLevel,
        Generation: (byte)pk.Generation,
        Format: pk.Extension,
        HeldItem: pk.HeldItem,
        IsEgg: pk.IsEgg,
        SourceLabel: sourceLabel);

    /// <summary>
    /// Reconstructs the exact original entity type from raw stored bytes + the recorded
    /// <see cref="BankEntryInfo.Format"/>, or <see langword="null"/> if the bytes no longer parse.
    /// </summary>
    /// <remarks>
    /// The recorded format is authoritative. <see cref="FileUtil.GetSupportedFile(System.Memory{byte}, System.ReadOnlySpan{char}, SaveFile?)"/>
    /// only uses the extension to break the 6/7, 8/8b and 9/9a ties; same-size formats are otherwise
    /// guessed from byte heuristics (a PK5 whose bytes look Gen 4-like is read as a PK4, garbling its
    /// nickname). Only formats without a fixed-size constructor (Gen 1/2) fall back to detection.
    /// </remarks>
    public static PKM? TryReconstruct(byte[] rawData, BankEntryInfo info)
    {
        try
        {
            return CreateExact(rawData, info.Format)
                ?? FileUtil.GetSupportedFile(rawData, info.Format) as PKM;
        }
        catch
        {
            return null;
        }
    }

    private static PKM? CreateExact(byte[] data, string? format) => format?.ToLowerInvariant() switch
    {
        "pk3" => new PK3(data),
        "ck3" => new CK3(data),
        "xk3" => new XK3(data),
        "pk4" => new PK4(data),
        "bk4" => new BK4(data),
        "rk4" => new RK4(data),
        "pk5" => new PK5(data),
        "pk6" => new PK6(data),
        "pk7" => new PK7(data),
        "pb7" => new PB7(data),
        "pk8" => new PK8(data),
        "pa8" => new PA8(data),
        "pb8" => new PB8(data),
        "pb8lumi" => new PB8LUMI(data), // Luminescent Platinum: same layout as PB8, wider species range
        "pk9" => new PK9(data),
        "pa9" => new PA9(data),
        _ => null,
    };
}
