using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class TrainerCard8DataSessionTests
{
    [Fact]
    public void PartyCopyReadsCurrentPartyAndOwnsOnlySelectedTeamsKnownFields()
    {
        var save = new SAV8SWSH();
        save.PartyData = [new PK8 { Species = 25 }];
        var session = new TrainerCard8DataSession(save);
        save.PartyData = [new PK8 { Species = 133, Gender = 1, EncryptionConstant = 42 }];
        session.CopyFromParty(false);
        Assert.Equal(133, session.Staged.TrainerCard.ViewPoke(0).Species);
        save.TrainerCard.ViewPoke(0).Form = 7;
        save.TrainerCard.ViewPoke(0).Unknown = 0xABCDEF12;
        save.TitleScreen.ViewPoke(0).Species = 25;
        Assert.True(session.Commit());
        Assert.Equal(133, save.TrainerCard.ViewPoke(0).Species);
        Assert.Equal(0, save.TrainerCard.ViewPoke(0).Form);
        Assert.Equal(0xABCDEF12u, save.TrainerCard.ViewPoke(0).Unknown);
        Assert.Equal(25, save.TitleScreen.ViewPoke(0).Species);
    }

    [Fact]
    public void CopyFromPartyPreservesNoncanonicalTrueShinyByteAndPadding()
    {
        var save = new SAV8SWSH();
        var pokemon = new PK8 { Species = 25 };
        Assert.True(pokemon.IsShiny);
        save.PartyData = [pokemon];
        int offset = TrainerCard8.GetPokeOffset(0);
        save.TrainerCard.Data[offset + 2] = 0xA5;
        save.TrainerCard.Data[offset + 0xC] = 7;
        save.State.Edited = false;
        var before = save.TrainerCard.Data.ToArray();
        var session = new TrainerCard8DataSession(save);
        session.CopyFromParty(false);
        Assert.Equal(7, session.Staged.TrainerCard.Data[offset + 0xC]);
        Assert.Equal(0xA5, session.Staged.TrainerCard.Data[offset + 2]);
        Assert.Equal(before, save.TrainerCard.Data.ToArray());
        Assert.False(save.State.Edited);
        Assert.True(session.Commit());
        Assert.Equal(25, save.TrainerCard.ViewPoke(0).Species);
        Assert.Equal(7, save.TrainerCard.Data[offset + 0xC]);
    }

    [Fact]
    public void NoOpCommitAndResetPreserveSourceAndEditedFlag()
    {
        var save = new SAV8SWSH();
        var before = save.TrainerCard.Data.ToArray();
        var session = new TrainerCard8DataSession(save);
        Assert.False(session.Commit());
        Assert.Equal(before, save.TrainerCard.Data.ToArray());
        Assert.False(save.State.Edited);
        session.Staged.TrainerCard.OT = "Changed";
        session.Reset();
        Assert.Equal(save.TrainerCard.OT, session.Staged.TrainerCard.OT);
        Assert.Equal(before, save.TrainerCard.Data.ToArray());
    }

    [Fact]
    public void TeamChangesRemainStagedAndCommitPreservesPaddingUnknownsAndIndependentFields()
    {
        var save = new SAV8SWSH();
        int offset = TrainerCard8.GetPokeOffset(0);
        save.TrainerCard.Data[offset + 2] = 0xA5;
        save.TrainerCard.ViewPoke(0).Unknown = 1234;
        var session = new TrainerCard8DataSession(save);
        session.Staged.TrainerCard.ViewPoke(0).Species = 25;
        session.Staged.TitleScreen.ViewPoke(0).Species = 133;
        Assert.Equal(0, save.TrainerCard.ViewPoke(0).Species);
        Assert.Equal(0, save.TitleScreen.ViewPoke(0).Species);
        save.TrainerCard.TrainerID = 123456;
        Assert.True(session.Commit());
        Assert.Equal(25, save.TrainerCard.ViewPoke(0).Species);
        Assert.Equal(133, save.TitleScreen.ViewPoke(0).Species);
        Assert.Equal(0xA5, save.TrainerCard.Data[offset + 2]);
        Assert.Equal(1234u, save.TrainerCard.ViewPoke(0).Unknown);
        Assert.Equal(123456, save.TrainerCard.TrainerID);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void CopyFromEmptyPartyPreservesUnknownSlotWordsAndCanBeDiscarded()
    {
        var save = new SAV8SWSH();
        save.TrainerCard.ViewPoke(0).Unknown = 1234;
        save.TitleScreen.ViewPoke(0).Unknown18 = 5678;
        var session = new TrainerCard8DataSession(save);
        session.CopyFromParty(false);
        session.CopyFromParty(true);
        Assert.Equal(1234u, session.Staged.TrainerCard.ViewPoke(0).Unknown);
        Assert.Equal(5678u, session.Staged.TitleScreen.ViewPoke(0).Unknown18);
        Assert.Equal(1234u, save.TrainerCard.ViewPoke(0).Unknown);
        Assert.Equal(5678u, save.TitleScreen.ViewPoke(0).Unknown18);
    }
}
