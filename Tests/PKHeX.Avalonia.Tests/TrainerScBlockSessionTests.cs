using System.Buffers.Binary;
using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class TrainerScBlockSessionTests
{
    internal static SAV8SWSH LoadPublicSave() => Assert.IsType<SAV8SWSH>(Fixtures.SaveFileFixture.LoadSave(
        Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8_sword_ct_public.main")));

    [Fact]
    public void PublicCrownTundraSaveDetectsAndReopensWithoutRepair()
    {
        var save = LoadPublicSave(); Assert.Equal(2, save.SaveRevision);
        var reopened = Assert.IsType<SAV8SWSH>(SaveUtil.GetSaveFile(save.Write()));
        Assert.Equal(save.OT, reopened.OT); Assert.Equal(save.Coordinates.M, reopened.Coordinates.M);
    }
    [Fact]
    public void CommitStagesChangedBlocksAndPreservesUnrelatedLiveChanges()
    {
        var save = LoadPublicSave(); var session = new TrainerScBlockDataSession<SAV8SWSH>(save);
        var original = save.MyStatus.Watt; session.Staged.MyStatus.Watt = original + 1;
        Assert.Equal(original, save.MyStatus.Watt); save.Money = 123;
        Assert.True(session.TryCommit()); Assert.Equal(original + 1, save.MyStatus.Watt); Assert.Equal(123u, save.Money);
    }
    [Fact]
    public void ConflictRejectsEveryWriteAndResetOrUndoDiscardDrafts()
    {
        var save = LoadPublicSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        var session = new TrainerScBlockDataSession<SAV8SWSH>(save);
        session.ApplyAction(s => s.MyStatus.Watt++); session.Undo(); Assert.True(session.TryCommit());
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        session.Staged.MyStatus.Watt++; session.Staged.Misc.BP++; save.MyStatus.Watt += 2;
        var live = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray()); Assert.False(session.TryCommit());
        foreach (var b in save.AllBlocks) Assert.Equal(live[b.Key], b.Data.ToArray());
        session.Reset(); Assert.True(session.TryCommit());
    }
}
