using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class ZaTrainerDataSessionTests
{
    private static SAV9ZA CreateSave() => Assert.IsType<SAV9ZA>(SaveUtil.GetSaveFile(File.ReadAllBytes(
        Path.Combine(SaveFileFixture.FindSaveFilesPath()!, "gen9a_legendsza.main"))));

    [Fact]
    public void FieldEditsStayStagedAndCommitWithoutCopyingUnrelatedLiveChanges()
    {
        var save = CreateSave(); var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        var session = new ZaTrainerDataSession(save);
        session.Staged.Coordinates.Map = "test_map";
        session.Staged.Coordinates.X = 12.5f;
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        save.Money = 12345;
        Assert.True(session.TryCommit());
        var reopened = new SAV9ZA(save.Write());
        Assert.Equal("test_map", reopened.Coordinates.Map);
        Assert.Equal(12.5f, reopened.Coordinates.X);
        Assert.Equal(12345u, reopened.Money);
    }

    [Fact]
    public void ConflictingMapUpdateRejectsTheEntireCommit()
    {
        var save = CreateSave(); var session = new ZaTrainerDataSession(save);
        session.Staged.Coordinates.X = 12.5f;
        session.Staged.OT = "Staged";
        save.Coordinates.Y = 35f;
        var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        save.State.Edited = false;
        Assert.False(session.TryCommit());
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void ResetAndNoOpCommitPreserveExactSourceAndEditedFlag()
    {
        var save = CreateSave(); var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        save.State.Edited = false;
        var session = new ZaTrainerDataSession(save);
        session.Staged.Coordinates.Map = "discard";
        session.Reset();
        Assert.True(session.TryCommit());
        Assert.False(save.State.Edited);
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
    }
}
