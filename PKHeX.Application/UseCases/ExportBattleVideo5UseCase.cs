using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>Exports a decrypted copy, keeping the encrypted live-save buffer untouched.</summary>
public sealed class ExportBattleVideo5UseCase
{
    public byte[] Execute(SAV5 save, int slot)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)slot, 4u);
        var result = save.GetBattleVideo(slot).ToArray();
        var video = new BattleVideo5(result) { IsDecrypted = BattleVideo5.GetIsDecrypted(result) };
        if (!video.IsUninitialized) video.Decrypt();
        return result;
    }
}
