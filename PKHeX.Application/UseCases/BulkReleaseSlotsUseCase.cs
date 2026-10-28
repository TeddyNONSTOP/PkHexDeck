using System.Collections.Generic;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>
/// Result of planning a bulk release. <see cref="Released"/> is the subset of the request that will be
/// blanked. <see cref="ExcludedParty"/> marked party members are never released (reported, not a
/// refusal). <see cref="Protected"/> counts marked box slots that are locked or overwrite-protected
/// (battle team, starter); any such slot refuses the whole batch — see <see cref="IsRefused"/>.
/// </summary>
public readonly record struct BulkReleaseResult(
    IReadOnlyList<SlotLocation> Released,
    int RequestedCount,
    int ExcludedParty,
    int Protected,
    int SkippedEmpty)
{
    /// <summary>True when at least one marked box slot is protected: nothing may be released.</summary>
    public bool IsRefused => Protected > 0;
}

/// <summary>
/// Releases (blanks) multiple marked box slots in one pass.
/// </summary>
/// <remarks>
/// Party slots are always excluded, mirroring this fork's existing single-item policy:
/// <c>MainWindowViewModel.OnPartyDeleteSlot</c> refuses a party release outright
/// (<c>Msg_CannotDeletePartyPokemon</c>), so the bulk path does not add a carve-out. Unlike the
/// skip-and-report transfers, release is the one bulk action that <b>refuses the whole batch</b> when
/// any marked box slot is locked or overwrite-protected (<see cref="SaveFile.IsBoxSlotOverwriteProtected"/>):
/// releasing is the irreversible-feeling operation, so the user must unmark the protected Pokémon
/// first rather than have a silently partial release.
/// </remarks>
public sealed class BulkReleaseSlotsUseCase
{
    public BulkReleaseResult Plan(SaveFile sav, IReadOnlyList<SlotLocation> sources)
    {
        var released = new List<SlotLocation>(sources.Count);
        var excludedParty = 0;
        var protectedCount = 0;
        var skippedEmpty = 0;

        foreach (var source in sources)
        {
            if (source.IsParty)
            {
                excludedParty++;
                continue;
            }

            if (!sav.HasBox || (uint)source.Box >= (uint)sav.BoxCount || (uint)source.Slot >= (uint)sav.BoxSlotCount
                || sav.GetBoxSlotAtIndex(source.Box, source.Slot).Species == 0)
            {
                skippedEmpty++;
                continue;
            }

            if (sav.IsBoxSlotOverwriteProtected(source.Box, source.Slot))
            {
                protectedCount++;
                continue;
            }

            released.Add(source);
        }

        if (protectedCount > 0)
            released.Clear();

        return new BulkReleaseResult(released, sources.Count, excludedParty, protectedCount, skippedEmpty);
    }

    /// <summary>Blanks every slot in <paramref name="released"/>. No undo recording of its own: the caller wraps it in <see cref="Services.UndoRedoService.ApplyBatch"/>.</summary>
    public void Execute(SaveFile sav, IReadOnlyList<SlotLocation> released)
    {
        foreach (var location in released)
            sav.SetBoxSlotAtIndex(sav.BlankPKM, location.Box, location.Slot);
    }
}
