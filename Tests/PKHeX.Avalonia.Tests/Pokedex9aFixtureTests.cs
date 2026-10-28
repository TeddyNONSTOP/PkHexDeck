using Moq;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Pokedex9aFixtureTests
{
    internal static SAV9ZA CreateSave(int revision)
    {
        var path = Path.Combine(SaveFileFixture.FindSaveFilesPath()!, "gen9a_legendsza.main");
        var save = Assert.IsType<SAV9ZA>(SaveUtil.GetSaveFile(File.ReadAllBytes(path)));
        save.SetValue(SaveBlockAccessor9ZA.KSaveRevision, (ulong)revision);
        return save;
    }
    private static Mock<IDialogService> Dialog(bool accepted = true)
    {
        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(accepted);
        return dialog;
    }
    [Theory]
    [InlineData(Species.Charizard, 0)] [InlineData(Species.Mewtwo, 0)]
    [InlineData(Species.Absol, 1)] [InlineData(Species.Lucario, 1)] [InlineData(Species.Garchomp, 2)]
    [InlineData(Species.Raichu, 1)] [InlineData(Species.Meowstic, 1)]
    [InlineData(Species.Magearna, 1)] [InlineData(Species.Tatsugiri, 1)]
    [InlineData(Species.Hoopa, 1)]
    public void EveryExoticFlagAndLanguageRoundtripsWithoutChangingOtherBlocks(Species chosen, int revision)
    {
        var save = CreateSave(revision); ushort species = (ushort)chosen;
        int offset = SpeciesConverter.GetInternal9(species) * PokeDexEntry9a.SIZE;
        save.Zukan.Data[offset + 0x30] = 0xA5; save.Zukan.Data[offset + 0x82] = 0x5A;
        save.Zukan.Data[offset + 0x09] |= 0xFC; save.Zukan.Data[offset + 0x10] |= 0xF8;
        uint dexKey = save.AllBlocks.Single(block => block.Data.Overlaps(save.Zukan.Data)).Key;
        var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        vm.SelectedSpecies = vm.FilteredSpecies.Single(item => item.Value == (int)chosen);
        var expected = vm.Flags.Select(row => !row.Value).ToArray();
        for (int i = 0; i < expected.Length; i++) vm.Flags[i].Value = expected[i];
        foreach (var row in vm.Languages) row.Value = true;
        foreach (var form in vm.Forms) { form.Seen = true; form.Caught = true; form.Shiny = true; }
        vm.DisplayForm = 0; vm.DisplayGender = (int)DisplayGender9a.Genderless;
        vm.DisplayShiny = true; vm.IsNew = true;
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        vm.SaveCommand.Execute(null);
        var reloaded = new SAV9ZA(save.Write());
        using var reopened = new Pokedex9aEditorViewModel(reloaded, Dialog().Object);
        reopened.SelectedSpecies = reopened.FilteredSpecies.Single(item => item.Value == (int)chosen);
        Assert.Equal(expected, reopened.Flags.Select(row => row.Value).ToArray());
        Assert.All(reopened.Languages, row => Assert.True(row.Value));
        Assert.All(reopened.Forms, row => { Assert.True(row.Caught); Assert.True(row.Seen); Assert.True(row.Shiny); });
        Assert.True(reopened.DisplayShiny); Assert.True(reopened.IsNew);
        Assert.Equal(0xA5, reloaded.Zukan.Data[offset + 0x30]); Assert.Equal(0x5A, reloaded.Zukan.Data[offset + 0x82]);
        Assert.Equal(0xFC, reloaded.Zukan.Data[offset + 0x09] & 0xFC); Assert.Equal(0xF8, reloaded.Zukan.Data[offset + 0x10] & 0xF8);
        foreach (var block in reloaded.AllBlocks.Where(block => block.Key != dexKey)) Assert.Equal(before[block.Key], block.Data.ToArray());
    }

    [Theory] [InlineData("SeenAll")] [InlineData("SeenNone")] [InlineData("CaughtAll")] [InlineData("CaughtNone")] [InlineData("Complete")]
    public async Task FixtureBulkScopesUndoAndCommitAreReal(string action)
    {
        var save = CreateSave(1); var before = save.Zukan.Data.ToArray();
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        vm.SelectedSpecies = vm.FilteredSpecies.Single(item => item.Value == (int)Species.Charizard);
        foreach (var form in vm.Forms) { form.Seen = action == "SeenNone"; form.Caught = action == "CaughtNone"; }
        var staged = vm.Forms.Select(row => (row.Caught, row.Seen, row.Shiny)).ToArray();
        vm.IncludeShiny = true;
        await vm.BulkCommand.ExecuteAsync(action);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        Assert.True(vm.CanUndo); vm.UndoCommand.Execute(null);
        Assert.Equal(staged, vm.Forms.Select(row => (row.Caught, row.Seen, row.Shiny)).ToArray());
        await vm.BulkCommand.ExecuteAsync(action); vm.SaveCommand.Execute(null);
        var reloaded = new SAV9ZA(save.Write());
        var entry = reloaded.Zukan.GetEntry((ushort)Species.Charizard);
        if (action == "SeenAll") Assert.True(entry.GetIsFormSeen(0));
        if (action == "SeenNone") Assert.False(entry.GetIsFormSeen(0));
        if (action is "CaughtAll" or "Complete") Assert.True(entry.GetIsFormCaught(0));
        if (action == "CaughtNone") Assert.False(entry.GetIsFormCaught(0));
    }

    [Theory] [InlineData("SeenAll")] [InlineData("CaughtAll")] [InlineData("Complete")]
    public async Task WholeDexScopeReachesEverySupportedSpeciesAndSurvivesSerialization(string action)
    {
        var save = CreateSave(1); var before = save.Zukan.Data.ToArray();
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        vm.WholeDex = true; vm.IncludeShiny = true;
        await vm.BulkCommand.ExecuteAsync(action);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        vm.SaveCommand.Execute(null);
        var reloaded = new SAV9ZA(save.Write());
        for (ushort species = 1; species <= reloaded.MaxSpeciesID; species++)
        {
            if (!reloaded.Personal.IsSpeciesInGame(species)) continue;
            var entry = reloaded.Zukan.GetEntry(species);
            if (action is "SeenAll" or "Complete") Assert.True(entry.IsSeen, $"Seen species {species}");
            if (action is "CaughtAll" or "Complete") Assert.True(entry.IsCaught, $"Caught species {species}");
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FixtureCancelAndWindowCloseDiscardEveryStagedChange(bool close)
    {
        var save = CreateSave(1); var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        vm.WholeDex = true; vm.IncludeShiny = true;
        await vm.BulkCommand.ExecuteAsync("Complete");
        if (close) vm.Dispose(); else vm.CancelCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
    }

    [Fact]
    public void ScatterbugFamilyExposesTwentyDistinctKnownFormStates()
    {
        using var vm = new Pokedex9aEditorViewModel(CreateSave(1), Dialog().Object);
        vm.SelectedSpecies = vm.FilteredSpecies.Single(item => item.Value == (int)Species.Spewpa);
        Assert.Equal(20, vm.Forms.Count);
        Assert.Equal(0xF_FFFFu, Pokedex9aCapabilities.GetFormMask((ushort)Species.Spewpa));
    }
}
