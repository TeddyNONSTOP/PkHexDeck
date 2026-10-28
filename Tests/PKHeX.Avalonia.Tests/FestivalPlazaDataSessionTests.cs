using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class FestivalPlazaDataSessionTests
{
    private static SAV7 Create(bool ultra)
    {
        SAV7 save = ultra ? new SAV7USUM() : new SAV7SM();
        save.SetRecord(38, 250); save.Festa.FestaCoins = 100;
        save.State.Edited = false; return save;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Editor_SaveCancelReset_KeepLiveSaveUntouchedUntilCommit(bool ultra)
    {
        var save = Create(ultra);
        save.Festa.FestivalPlazaName = "Original";
        save.State.Edited = false;
        var before = save.Data.ToArray();
        var editor = new FestivalPlazaEditorViewModel(save);
        Assert.Equal(250, editor.UsedFC);
        editor.PlazaName = "Discard"; editor.UsedFC = 500;
        editor.Facilities[0].OwnerName = "Visitor";
        Assert.Equal(before, save.Data.ToArray());
        editor.RefreshCommand.Execute(null);
        Assert.Equal("Original", editor.PlazaName);
        Assert.Equal(250, editor.UsedFC);
        editor.PlazaName = "Cancelled";
        editor.CancelCommand.Execute(null);
        editor.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());
        Assert.False(save.State.Edited);
        editor = new FestivalPlazaEditorViewModel(save);
        editor.CurrentFC = 200; editor.UsedFC = 600;
        editor.Facilities[0].OwnerName = "Visitor";
        var closed = false; editor.CloseRequested = () => closed = true;
        editor.SaveCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(200, save.Festa.FestaCoins);
        Assert.Equal(600, save.GetRecord(38));
        Assert.Equal(800, save.Festa.TotalFestaCoins);
        Assert.Equal("Visitor", save.Festa.GetFestaFacility(0).OriginalTrainerName);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Editor_OpeningAndSavingWithoutEdits_PreservesRawNamePadding(bool ultra)
    {
        var save = Create(ultra);
        save.Festa.FestivalPlazaName = "Plaza";
        save.Festa.Data[0x538] = 0xA7;
        save.Festa.GetFestaFacility(0).OriginalTrainerName = "Visitor";
        save.Festa.Data[0x32C] = 0xD5;
        save.State.Edited = false;
        var before = save.Data.ToArray();
        var editor = new FestivalPlazaEditorViewModel(save);
        editor.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());
        Assert.False(save.State.Edited);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CurrencyAndFacility_StageAndCommit_WithCorrectCollectedCoins(bool ultra)
    {
        var save = Create(ultra); var before = save.Data.ToArray();
        var session = new FestivalPlazaDataSession(save);
        session.WorkingSave.SetRecord(38, 400);
        session.WorkingSave.Festa.FestaCoins = 150;
        session.WorkingSave.Festa.GetFestaFacility(0).NPC = 7;
        Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
        Assert.True(session.TryCommit(out var changed)); Assert.True(changed);
        Assert.Equal(150, save.Festa.FestaCoins); Assert.Equal(400, save.GetRecord(38));
        Assert.Equal(550, save.Festa.TotalFestaCoins); Assert.Equal(7, save.Festa.GetFestaFacility(0).NPC);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResetAndNoOp_PreserveSaveBytesAndExistingEditedFlag(bool edited)
    {
        var save = Create(false); save.State.Edited = edited;
        var before = save.Data.ToArray(); var session = new FestivalPlazaDataSession(save);
        session.WorkingSave.Festa.FestivalPlazaName = "Changed";
        session.WorkingSave.SetRecord(38, 600);
        session.Reset(); Assert.True(session.TryCommit(out var changed)); Assert.False(changed);
        Assert.Equal(before, save.Data.ToArray()); Assert.Equal(edited, save.State.Edited);
    }
    [Fact]
    public void Commit_PreservesIndependentCurrencyRecordMoneyAndUntouchedBytes()
    {
        var save = Create(true); var session = new FestivalPlazaDataSession(save);
        session.WorkingSave.Festa.FestaCoins = 200;
        session.WorkingSave.Festa.GetFestaFacility(0).Type = 125;
        save.SetRecord(38, 700); save.Money = 123456;
        save.SetRecord(39, 456);
        save.Festa.Data[0x600] = 0xA7;
        Assert.True(session.TryCommit(out _));
        Assert.Equal(700, save.GetRecord(38)); Assert.Equal(900, save.Festa.TotalFestaCoins);
        Assert.Equal(123456u, save.Money); Assert.Equal(456, save.GetRecord(39));
        Assert.Equal(0xA7, save.Festa.Data[0x600]); Assert.Equal(125, save.Festa.GetFestaFacility(0).Type);
    }
    [Fact]
    public void InvalidCurrency_FailsBeforeAnyWrite()
    {
        var save = Create(false); var before = save.Data.ToArray();
        var session = new FestivalPlazaDataSession(save);
        session.WorkingSave.Festa.FestivalPlazaName = "Changed";
        session.WorkingSave.Festa.FestaCoins = -1;
        Assert.False(session.TryCommit(out var changed)); Assert.False(changed);
        Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
    }
}
