using PKHeX.Core;

namespace PKHeX.Application.Models;

/// <summary>Save-owned slot metadata, independent of the sprite's species and form.</summary>
public readonly record struct SpriteSlotState(bool Illegal, bool MoveHint, StorageSlotSource Storage)
{
    public static SpriteSlotState Create(PKM pk, SaveFile save, StorageSlotType origin, StorageSlotSource storage, bool haXMode)
    {
        if (pk.Species == 0) return new(false, false, storage);
        var illegal = !haXMode && !new LegalityAnalysis(pk, save.Personal, origin).Valid;
        return new(illegal, !haXMode && !illegal && pk.Format >= 8 && MoveInfo.IsDummiedMoveAny(pk), storage);
    }
}
