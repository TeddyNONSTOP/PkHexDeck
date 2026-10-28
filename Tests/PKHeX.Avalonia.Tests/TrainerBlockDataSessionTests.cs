using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class TrainerBlockDataSessionTests
{
    [Fact]
    public void LGPEMapEditsCommitWithoutOverwritingAnIndependentMoneyChange()
    {
        var save = new SAV7b(); var before = save.Data.ToArray();
        var session = new TrainerBlockDataSession<SAV7b>(save);
        session.Staged.Coordinates.X = 12.5f;
        Assert.Equal(before, save.Data.ToArray());
        save.Money = 12345;
        Assert.True(session.TryCommit());
        Assert.Equal(12.5f, save.Coordinates.X); Assert.Equal(12345u, save.Money);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void AConflictingMapBlockRejectsTrainerAndMapWritesTogether()
    {
        var save = new SAV7b(); var session = new TrainerBlockDataSession<SAV7b>(save);
        save.Coordinates.Y = 35;
        var before = save.Data.ToArray(); save.State.Edited = false;
        Assert.False(session.TryCommit(staged => { staged.Coordinates.X = 12; staged.OT = "Rejected"; }));
        Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
        Assert.True(session.TryCommit());
        Assert.Equal(before, save.Data.ToArray());
    }

    [Fact]
    public void BulkUndoResetAndNoOpLeaveTheOriginalBytesUntouched()
    {
        var save = new SAV7b(); var before = save.Data.ToArray(); save.State.Edited = false;
        var session = new TrainerBlockDataSession<SAV7b>(save);
        session.ApplyAction(staged => staged.EventWork.UnlockAllTitleFlags());
        Assert.True(session.CanUndo); Assert.Equal(before, save.Data.ToArray());
        session.Undo(); Assert.Equal(before, session.Staged.Data.ToArray());
        session.Reset(); Assert.True(session.TryCommit()); Assert.False(save.State.Edited);
        Assert.Equal(before, save.Data.ToArray());
    }
}
