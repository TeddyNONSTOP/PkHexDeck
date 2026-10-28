using System;
using System.Linq;

namespace PKHeX.Core;

/// <summary>
/// Defeated Status for all trainers (Dpr.Trainer.TrainerID), Luminescent Platinum revision.
/// </summary>
/// <remarks>
/// Same declared block size (0x1618) and offset as <see cref="BattleTrainerStatus8b"/>, but repacked to 2 bits per
/// trainer (was 32 bits per trainer) to fit 5656 trainers (was 707) in the same space: 5656/4 = 1414 bytes &lt;= 0x1618.
/// </remarks>
public sealed class BattleTrainerStatus8bLumi(SAV8BSLuminescent sav, Memory<byte> raw) : SaveBlock<SAV8BSLuminescent>(sav, raw)
{
    // Structure: (bool IsWin, bool IsBattleSearcher)[5656], 2 bits per trainer, 4 trainers per byte.
    private const int COUNT_TRAINER = 5656;

    public bool AnyDefeated => Enumerable.Range(0, COUNT_TRAINER).Any(GetIsWin);
    public bool AnyUndefeated => Enumerable.Range(0, COUNT_TRAINER).Any(z => !GetIsWin(z));

    /// <summary>
    /// Don't use this unless you've finished the post-game.
    /// </summary>
    public void DefeatAll()
    {
        for (int i = 0; i < COUNT_TRAINER; i++)
        {
            SetIsWin(i, true);
            SetIsBattleSearcher(i, false);
        }
    }

    /// <summary>
    /// Don't use this unless you've finished the post-game.
    /// </summary>
    public void RebattleAll()
    {
        for (int i = 0; i < COUNT_TRAINER; i++)
        {
            SetIsWin(i, false);
            SetIsBattleSearcher(i, true);
        }
    }

    private static int GetTrainerStructOffset(int trainer)
    {
        if ((uint)trainer >= COUNT_TRAINER)
            throw new ArgumentOutOfRangeException(nameof(trainer));
        return trainer / 4;
    }

    private void SetBit(int byteOffset, int bitIndex, bool value)
    {
        Data[byteOffset] = (byte)((Data[byteOffset] & ~(1 << bitIndex)) | ((value ? 1 : 0) << bitIndex));
    }

    public bool GetIsWin(int trainer) => ((Data[GetTrainerStructOffset(trainer)] >> (trainer % 4 * 2)) & 1) == 1;
    public bool GetIsBattleSearcher(int trainer) => ((Data[GetTrainerStructOffset(trainer)] >> (trainer % 4 * 2 + 1)) & 1) == 1;
    public void SetIsWin(int trainer, bool value) => SetBit(GetTrainerStructOffset(trainer), trainer % 4 * 2, value);
    public void SetIsBattleSearcher(int trainer, bool value) => SetBit(GetTrainerStructOffset(trainer), trainer % 4 * 2 + 1, value);
}
