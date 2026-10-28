using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class DonutDataSessionTests
{
    [Fact]
    public void NoOpBulkDoesNotClaimOrOverwriteIndependentRecords()
    {
        var save = new SAV9ZA(); var session = new DonutDataSession(save);
        session.ApplyBulk(_ => { });
        Assert.False(session.CanUndo);
        save.Donuts.GetDonut(10).Stars = 5;
        Assert.True(session.TryCommit());
        Assert.Equal(5, save.Donuts.GetDonut(10).Stars);
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void WholePocketOperationPreflightsUnchangedSlotsAndUndoRestoresScope()
    {
        var save = new SAV9ZA(); var session = new DonutDataSession(save);
        session.ApplyBulk(pocket => pocket.GetDonut(0).Stars = 3);
        save.Donuts.GetDonut(10).Stars = 5;
        Assert.False(session.TryCommit()); Assert.Equal(0, save.Donuts.GetDonut(0).Stars);
        session.Undo(); Assert.True(session.TryCommit());
        Assert.Equal(5, save.Donuts.GetDonut(10).Stars);
    }

    [Fact]
    public void NonzeroTimestampMeansOccupiedAndCompressKeepsOccupiedRecords()
    {
        var save = new SAV9ZA();
        var donut = save.Donuts.GetDonut(3); donut.MillisecondsSince1970 = 1; donut.Flavor0 = ulong.MaxValue; donut.Reserved = 0x12345678;
        var before = save.Donuts.Data.ToArray(); save.State.Edited = false;
        var session = new DonutDataSession(save);
        Assert.False(DonutDataSession.IsOccupied(session.Pocket.GetDonut(0)));
        Assert.True(DonutDataSession.IsOccupied(session.Pocket.GetDonut(3)));
        session.Compress();
        Assert.True(DonutDataSession.IsOccupied(session.Pocket.GetDonut(0)));
        Assert.Equal(ulong.MaxValue, session.Pocket.GetDonut(0).Flavor0);
        Assert.Equal(0x12345678ul, session.Pocket.GetDonut(0).Reserved);
        Assert.Equal(before, save.Donuts.Data.ToArray()); Assert.False(save.State.Edited);
        session.Undo(); Assert.Equal(before, session.Pocket.Data.ToArray());
        Assert.True(session.TryCommit()); Assert.Equal(before, save.Donuts.Data.ToArray()); Assert.False(save.State.Edited);
    }
    [Fact]
    public void ExactRecordImportValidationAndResetPreserveSource()
    {
        var save = new SAV9ZA(); var before = save.Donuts.Data.ToArray();
        var session = new DonutDataSession(save);
        Assert.False(session.ImportRecord(0, new byte[Donut9a.Size - 1]));
        Assert.False(session.ImportRecord(0, new byte[Donut9a.Size + 1]));
        var bytes = new byte[Donut9a.Size]; bytes[0] = 1; bytes[^1] = 0xA5;
        Assert.True(session.ImportRecord(0, bytes)); Assert.Equal(bytes, session.ExportRecord(0));
        Assert.Equal(before, save.Donuts.Data.ToArray());
        session.ResetRecord(0); Assert.Equal(before, session.Pocket.Data.ToArray());
        session.ImportRecord(0, bytes); Assert.True(session.TryCommit());
        Assert.Equal(bytes, save.Donuts.GetDonut(0).Data.ToArray()); Assert.True(save.State.Edited);
    }
    [Fact]
    public void RecordConflictRejectsEntireCommitAndUnrelatedRecordIsPreserved()
    {
        var save = new SAV9ZA(); var session = new DonutDataSession(save);
        session.Pocket.GetDonut(0).Stars = 3; session.Pocket.GetDonut(1).Calories = 500;
        save.Donuts.GetDonut(0).Stars = 4;
        Assert.False(session.TryCommit()); Assert.Equal(0, save.Donuts.GetDonut(1).Calories);
        session.Reset(); session.Pocket.GetDonut(0).Stars = 2;
        save.Donuts.GetDonut(5).Reserved = ulong.MaxValue;
        Assert.True(session.TryCommit()); Assert.Equal(ulong.MaxValue, save.Donuts.GetDonut(5).Reserved);
    }
}
