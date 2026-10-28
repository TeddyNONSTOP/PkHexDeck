using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class SwshTrainerFieldTests
{
    [Fact]
    public void InvalidStoredDatesAndGeometrySurviveNoOpButInvalidEditsCannotCommit()
    {
        var save = TrainerScBlockSessionTests.LoadPublicSave();
        save.TrainerCard.StartedMonth = 0;
        save.Played.LastSavedDate = null;
        save.Coordinates.SX = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234));
        var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new Misc8EditorViewModel(save);
        Assert.Null(vm.StartedDate); Assert.Null(vm.LastSavedDate); Assert.True(double.IsNaN(vm.SX));
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.XText = "invalid"; vm.LeagueCardName = "Blocked";
        Assert.False(vm.CanSave); vm.SaveCommand.Execute(null); Assert.True(vm.HasError);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.ResetCommand.Execute(null);
        vm.LastSavedDate = new DateTimeOffset(1899, 12, 31, 0, 0, 0, TimeSpan.Zero);
        vm.LastSavedTime = TimeSpan.Zero; Assert.False(vm.CanSave);
        vm.LastSavedDate = new DateTimeOffset(5996, 1, 1, 0, 0, 0, TimeSpan.Zero); Assert.False(vm.CanSave);
        vm.ResetCommand.Execute(null); Assert.True(vm.CanSave);
    }
    [Fact]
    public void IdentityCardMapAndDatesRoundtripOnlyAfterApply()
    {
        var save = TrainerScBlockSessionTests.LoadPublicSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new Misc8EditorViewModel(save);
        vm.TrainerName = "Tester"; vm.LeagueCardName = "Card"; vm.UniformNumber = "123"; vm.LeagueTrainerId = 456789; vm.RotoRallyScore = 234;
        vm.Bp = 12; vm.Watts = 345; vm.MapId = 0x123456789abcdef0ul; vm.X = 10; vm.Y = -20; vm.Z = 30;
        vm.SX = 1.5; vm.SY = 2; vm.SZ = 0.5; vm.Rotation = 135;
        vm.StartedDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        vm.LastSavedDate = vm.StartedDate; vm.LastSavedTime = new TimeSpan(13, 14, 0);
        Assert.True(vm.CanSave); foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        var reopened = Assert.IsType<SAV8SWSH>(SaveUtil.GetSaveFile(save.Write())); using var check = new Misc8EditorViewModel(reopened);
        Assert.Equal("Tester", check.TrainerName); Assert.Equal("Card", check.LeagueCardName); Assert.Equal("123", check.UniformNumber);
        Assert.Equal(456789, check.LeagueTrainerId); Assert.Equal(234, check.RotoRallyScore); Assert.Equal(12, check.Bp);
        Assert.Equal(0x123456789abcdef0ul, check.MapId); Assert.Equal(-20, check.Y); Assert.Equal(1.5, check.SX); Assert.Equal(135, check.Rotation, 4);
        Assert.Equal(vm.StartedDate, check.StartedDate); Assert.Equal(vm.LastSavedTime, check.LastSavedTime);
        var after = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray()); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(after[b.Key], b.Data.ToArray());
    }
    [Fact]
    public void NoOpAndResetPreserveSourceAndUnrelatedLiveChanges()
    {
        var save = TrainerScBlockSessionTests.LoadPublicSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new Misc8EditorViewModel(save); vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.LeagueCardName = "Discard"; vm.X = 88; vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.X += 1; save.Money = 123; vm.SaveCommand.Execute(null); Assert.False(vm.HasError); Assert.Equal(123u, save.Money);
    }
    [Fact]
    public async Task TrainerCardAndTrainerDraftsCoordinateThroughConflictsInsteadOfOverwriting()
    {
        var save = TrainerScBlockSessionTests.LoadPublicSave(); using var trainer = new Misc8EditorViewModel(save);
        using var card = new TrainerCard8EditorViewModel(save); trainer.LeagueCardName = "Trainer"; card.TrainerName = "Card";
        await card.SaveCommand.ExecuteAsync(null); Assert.Equal("Card", save.TrainerCard.OT);
        var after = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray()); trainer.SaveCommand.Execute(null); Assert.True(trainer.HasError);
        foreach (var b in save.AllBlocks) Assert.Equal(after[b.Key], b.Data.ToArray());
        trainer.ResetCommand.Execute(null); trainer.X += 1; trainer.SaveCommand.Execute(null); Assert.False(trainer.HasError); Assert.Equal("Card", save.TrainerCard.OT);
        using var second = new TrainerCard8EditorViewModel(save); second.Number = "321";
        trainer.LeagueCardName = "New"; trainer.SaveCommand.Execute(null); Assert.False(trainer.HasError);
        await second.SaveCommand.ExecuteAsync(null); Assert.Equal("New", save.TrainerCard.OT); Assert.Equal("321", save.TrainerCard.Number);
    }
}
