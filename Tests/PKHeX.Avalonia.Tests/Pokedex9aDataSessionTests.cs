using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class Pokedex9aDataSessionTests
{
    [Fact]
    public void UnknownLanguageGenderAndMegaBitsSurviveAStagedClear()
    {
        var save = new SAV9ZA();
        save.Zukan.Data[0x09] = 0xFC; save.Zukan.Data[0x0B] = 0xF8; save.Zukan.Data[0x10] = 0xF8;
        var session = new Pokedex9aDataSession(save);
        session.Dex.GetEntry(0).Clear();
        Assert.True(session.TryCommit());
        Assert.Equal(0xFC, save.Zukan.Data[0x09]);
        Assert.Equal(0xF8, save.Zukan.Data[0x0B]); Assert.Equal(0xF8, save.Zukan.Data[0x10]);
    }

    [Fact]
    public void FormAndIndependentLiveFlagsCommitWithoutTouchingOpaqueBytes()
    {
        var save = new SAV9ZA();
        var before = save.Zukan.Data.ToArray();
        var session = new Pokedex9aDataSession(save);
        session.Dex.GetEntry(25).SetIsFormSeen(0, true);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        save.Zukan.GetEntry(25).SetIsFormSeen(1, true);
        save.Zukan.Data[0x30] = 0xA5;
        Assert.True(session.TryCommit());
        Assert.True(save.Zukan.GetEntry(25).GetIsFormSeen(0));
        Assert.True(save.Zukan.GetEntry(25).GetIsFormSeen(1));
        Assert.Equal(0xA5, save.Zukan.Data[0x30]);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void DisplayConflictPreflightsBeforeAnyFlagIsWritten()
    {
        var save = new SAV9ZA();
        var session = new Pokedex9aDataSession(save);
        session.Dex.GetEntry(25).SetIsFormCaught(0, true);
        session.Dex.GetEntry(25).DisplayForm = 1;
        save.Zukan.GetEntry(25).DisplayForm = 2;
        Assert.False(session.TryCommit());
        Assert.False(save.Zukan.GetEntry(25).GetIsFormCaught(0));
        Assert.Equal(2, save.Zukan.GetEntry(25).DisplayForm);
    }

    [Fact]
    public void ResetAndNoOpRetainExactDataAndEditedState()
    {
        var save = new SAV9ZA();
        save.Zukan.Data[0x0A] = 0xA5;
        var before = save.Zukan.Data.ToArray(); save.State.Edited = false;
        var session = new Pokedex9aDataSession(save);
        session.Dex.GetEntry(0).SetDisplayIsNew(false);
        session.Dex.GetEntry(0).SetDisplayIsNew(true);
        Assert.True(session.TryCommit());
        Assert.Equal(before, save.Zukan.Data.ToArray()); Assert.False(save.State.Edited);
        session.Dex.GetEntry(25).SetIsSeenAlpha(true); session.Reset();
        Assert.Equal(before, session.Dex.Data.ToArray());
    }
}
