using Moq;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class TrainerCard8WorkflowTests
{
    [Fact]
    public async Task StartDateCommitsAsAnAtomicTuple()
    {
        var save = CreateSave(); save.TrainerCard.StartedYear = 1990;
        save.TrainerCard.StartedMonth = 10; save.TrainerCard.StartedDay = 1;
        using var vm = new TrainerCard8EditorViewModel(save);
        vm.StartedDateText = "1991-10-01";
        save.TrainerCard.StartedDay = 2;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1991, save.TrainerCard.StartedYear);
        Assert.Equal(10, save.TrainerCard.StartedMonth);
        Assert.Equal(1, save.TrainerCard.StartedDay);
    }

    [Fact]
    public async Task SemanticRevertsRestoreOriginalTrashNoncanonicalFlagsAndInvalidStoredDate()
    {
        var save = CreateSave();
        save.SetString(save.TrainerCard.OriginalTrainerTrash, "ABCDEFGHIJKLM", 13, StringConverterOption.ClearZero);
        save.TrainerCard.StartedYear = 65535; save.TrainerCard.StartedMonth = 255;
        save.TrainerCard.Data[0x30] = 7;
        save.TrainerCard.ViewPoke(0).Species = 25;
        save.TrainerCard.Data[TrainerCard8.GetPokeOffset(0) + 0xC] = 7;
        save.State.Edited = false; var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        Assert.True(vm.CanSave); Assert.True(vm.HasInvalidStoredDate);
        vm.TrainerName = "Changed"; vm.TrainerName = "ABCDEFGHIJKLM";
        vm.PokeDexComplete = true; vm.PokeDexComplete = false;
        vm.CardTeam[0].IsShiny = false; vm.CardTeam[0].IsShiny = true;
        vm.StartedDateText = "2020-02-29"; vm.StartedDateText = string.Empty;
        Assert.True(vm.CanSave);
        await vm.SaveCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
    }

    internal static SAV8SWSH CreateSave(int revision = 2, GameVersion version = GameVersion.SW)
    {
        var save = Raid8TransactionTests.CreateSave(revision);
        save.Version = version;
        // The synthetic fixture supplies object blocks; this known scalar is explicitly typed.
        save.Blocks.GetBlock(SaveBlockAccessor8SWSH.KRotoRally).ChangeStoredType(SCTypeCode.UInt32);
        save.State.Edited = false;
        return save;
    }
    internal static Dictionary<uint, byte[]> Snapshot(SAV8SWSH save) => save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
    internal static void AssertUnchanged(SAV8SWSH save, Dictionary<uint, byte[]> before)
    { foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray()); }

    [Theory]
    [InlineData(0, GameVersion.SW)]
    [InlineData(2, GameVersion.SH)]
    public async Task SentinelsUnknownBytesAndLegitimateMaximaSurviveNoOpSave(int revision, GameVersion version)
    {
        var save = CreateSave(revision, version);
        save.TrainerCard.PokeDexOwned = ushort.MaxValue;
        save.TrainerCard.ShinyPokemonFound = ushort.MaxValue;
        save.TrainerCard.CurryTypesOwned = ushort.MaxValue;
        save.TrainerCard.CaughtPokemon = -1;
        save.TrainerCard.RotoRallyScore = TrainerCard8.RotoRallyScoreMax;
        save.TrainerCard.Starter = 255;
        save.TrainerCard.Data[0x31] = 0xA5;
        save.TrainerCard.Data[0x30] = 7;
        save.State.Edited = false;
        var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        Assert.All(vm.Statistics.Take(4), row => Assert.True(row.IsUnset));
        Assert.False(vm.Statistics.Single(row => row.Id == "Rally").IsUnset);
        Assert.Contains(vm.StarterOptions, option => option.Value == 255);
        await vm.SaveCommand.ExecuteAsync(null);
        AssertUnchanged(save, before);
        Assert.False(save.State.Edited);
    }

    [Fact]
    public async Task BothTeamsAndMetadataStayStagedUntilSaveAndRoundtrip()
    {
        var save = CreateSave();
        save.TrainerCard.ViewPoke(0).Unknown = 0x12345678;
        save.TitleScreen.ViewPoke(0).Unknown18 = 0xABCDEF12;
        save.TrainerCard.Data[TrainerCard8.GetPokeOffset(0) + 2] = 0xA5;
        var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        vm.TrainerName = "Trainer"; vm.Number = "123";
        vm.StartedDateText = "2020-02-29";
        vm.Metadata.Single(row => row.Id == "TrainerId").Value = 654321;
        vm.Metadata.Single(row => row.Id == "Printed").Value = uint.MaxValue;
        vm.Appearance.Single(row => row.Id == "Hair").Value = ulong.MaxValue;
        vm.CardTeam[0].Species = 25; vm.CardTeam[0].Form = 2; vm.CardTeam[0].Gender = 1;
        vm.CardTeam[0].IsShiny = true; vm.CardTeam[0].EncryptionConstant = uint.MaxValue; vm.CardTeam[0].FormArgument = int.MinValue;
        vm.TitleTeam[0].Species = 133; vm.TitleTeam[0].FormArgument = -1;
        AssertUnchanged(save, before);
        Assert.True(vm.CanSave);
        // An independent field change must remain intact.
        save.TrainerCard.Language = 7;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(save.State.Edited);
        var roundtrip = new SAV8SWSH(save.Write());
        Assert.Equal("Trainer", roundtrip.TrainerCard.OT);
        Assert.Equal("123", roundtrip.TrainerCard.Number);
        Assert.Equal(654321, roundtrip.TrainerCard.TrainerID);
        Assert.Equal(7, roundtrip.TrainerCard.Language);
        Assert.Equal(2020, roundtrip.TrainerCard.StartedYear);
        Assert.Equal(29, roundtrip.TrainerCard.StartedDay);
        Assert.Equal(uint.MaxValue, roundtrip.TrainerCard.TimestampPrinted);
        Assert.Equal(ulong.MaxValue, roundtrip.TrainerCard.Hair);
        Assert.Equal(25, roundtrip.TrainerCard.ViewPoke(0).Species);
        Assert.Equal(int.MinValue, roundtrip.TrainerCard.ViewPoke(0).FormArgument);
        Assert.Equal(0xA5, roundtrip.TrainerCard.Data[TrainerCard8.GetPokeOffset(0) + 2]);
        Assert.Equal(0x12345678u, roundtrip.TrainerCard.ViewPoke(0).Unknown);
        Assert.Equal(133, roundtrip.TitleScreen.ViewPoke(0).Species);
        Assert.Equal(0xABCDEF12u, roundtrip.TitleScreen.ViewPoke(0).Unknown18);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancelAndDisposeDiscardCopyAndDirectEdits(bool cancel)
    {
        var save = CreateSave(); save.PartyData = [new PK8 { Species = 25 }]; save.State.Edited = false;
        var before = Snapshot(save);
        var vm = new TrainerCard8EditorViewModel(save);
        vm.TrainerName = "Changed"; vm.SetPartyToCardCommand.Execute(null); vm.SetPartyToTitleCommand.Execute(null);
        Assert.Equal(25, vm.CardTeam[0].Species); Assert.Equal(25, vm.TitleTeam[0].Species);
        if (cancel) vm.CancelCommand.Execute(null); else vm.Dispose();
        vm.CardTeam[0].Species = 133;
        vm.SaveCommand.Execute(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited); Assert.False(vm.CanSave);
    }

    [Fact]
    public void IndependentPartyCopyAndResetPreserveOtherTeamAndSelections()
    {
        var save = CreateSave(); save.PartyData = [new PK8 { Species = 25 }]; save.State.Edited = false;
        var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        vm.SelectedCardPokemon = vm.CardTeam[4]; vm.SelectedTitlePokemon = vm.TitleTeam[3];
        vm.TitleTeam[0].Species = 133;
        vm.SetPartyToCardCommand.Execute(null);
        Assert.Equal(25, vm.CardTeam[0].Species); Assert.Equal(133, vm.TitleTeam[0].Species);
        Assert.Equal(4, vm.SelectedCardPokemon!.Index); Assert.Equal(3, vm.SelectedTitlePokemon!.Index);
        vm.RefreshCommand.Execute(null);
        Assert.Equal(0, vm.CardTeam[0].Species); Assert.Equal(0, vm.TitleTeam[0].Species);
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
    }

    [Fact]
    public async Task RallyUpdatesBothStoredValuesOnlyOnCommit()
    {
        var save = CreateSave(); var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        vm.Statistics.Single(row => row.Id == "Rally").Value = 99999;
        AssertUnchanged(save, before);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(99999, save.TrainerCard.RotoRallyScore);
        Assert.Equal(99999u, save.GetValue<uint>(SaveBlockAccessor8SWSH.KRotoRally));
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void InvalidDateNumberAndStatisticsBlockSaveWithoutSourceWrites()
    {
        var save = CreateSave(); var before = Snapshot(save);
        using var vm = new TrainerCard8EditorViewModel(save);
        vm.StartedDateText = "2020-02-30"; Assert.False(vm.CanSave);
        vm.StartedDateText = "2020-02-29"; Assert.True(vm.CanSave);
        vm.Number = "abc"; Assert.False(vm.CanSave); vm.Number = "123";
        var caught = vm.Statistics.Single(row => row.Id == "Caught");
        caught.Value = 100000; Assert.False(vm.CanSave); caught.Value = 99999; Assert.True(vm.CanSave);
        var rally = vm.Statistics.Single(row => row.Id == "Rally");
        rally.Value = -1; Assert.False(vm.CanSave); rally.Value = 99999; Assert.True(vm.CanSave);
        var curry = vm.Statistics.Single(row => row.Id == "Curry");
        curry.Value = 152; Assert.False(vm.CanSave); curry.Value = 151; Assert.True(vm.CanSave);
        AssertUnchanged(save, before);
    }

    [Fact]
    public async Task UnsupportedRallyTypeFailsBeforeAnyCardOrTeamWrite()
    {
        var save = new SAV8SWSH(); var before = Snapshot(save);
        var dialog = new Mock<IDialogService>();
        using var vm = new TrainerCard8EditorViewModel(save, dialogs: dialog.Object);
        vm.TrainerName = "Changed"; vm.CardTeam[0].Species = 25;
        vm.Statistics.Single(row => row.Id == "Rally").Value = 100;
        await vm.SaveCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited); Assert.True(vm.CanSave);
        dialog.Verify(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}
