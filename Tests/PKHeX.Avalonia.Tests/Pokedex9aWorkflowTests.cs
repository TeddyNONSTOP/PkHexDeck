using Moq;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Pokedex9aWorkflowTests
{
    private static Mock<IDialogService> Dialog(bool answer = true)
    {
        var result = new Mock<IDialogService>();
        result.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(answer);
        return result;
    }

    [Fact]
    public void DirectFormsLanguagesAndDisplayStayStagedAndCommitToRealDex()
    {
        var save = new SAV9ZA(); var before = save.Zukan.Data.ToArray();
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        Assert.True(vm.IsSupported);
        vm.SelectedSpecies = vm.FilteredSpecies.Single(item => item.Value == (int)Species.Pikachu);
        vm.Forms[0].Caught = true; vm.Forms[0].Seen = true; vm.Forms[0].Shiny = true;
        Assert.Equal(10, vm.Languages.Count); vm.Languages[^1].Value = true;
        vm.DisplayGender = (int)DisplayGender9a.Female; vm.DisplayShiny = true; vm.IsNew = true;
        Assert.Equal(before, save.Zukan.Data.ToArray());
        vm.SaveCommand.Execute(null);
        Assert.True(save.Zukan.GetEntry((ushort)Species.Pikachu).GetIsFormCaught(0));
        Assert.True(save.Zukan.GetEntry((ushort)Species.Pikachu).GetLanguageFlag((int)LanguageID.SpanishL));
        Assert.False(save.Zukan.GetEntry((ushort)Species.Pikachu).GetLanguageFlag((int)LanguageID.Spanish));
        Assert.True(save.Zukan.GetEntry((ushort)Species.Pikachu).GetDisplayIsShiny());
        Assert.True(save.State.Edited);
    }

    [Theory]
    [InlineData("SeenAll", true, false)]
    [InlineData("CaughtAll", false, true)]
    public async Task NamedBulkActionsTouchOnlyNamedFormFlagsAndAreUndoable(string action, bool seen, bool caught)
    {
        var save = new SAV9ZA();
        using var vm = new Pokedex9aEditorViewModel(save, Dialog().Object);
        vm.SelectedSpecies = vm.FilteredSpecies.Single(item => item.Value == (int)Species.Pikachu);
        await vm.BulkCommand.ExecuteAsync(action);
        Assert.Equal(seen, vm.Forms[0].Seen); Assert.Equal(caught, vm.Forms[0].Caught);
        Assert.False(save.Zukan.GetEntry((ushort)Species.Pikachu).IsSeen);
        Assert.True(vm.CanUndo); vm.UndoCommand.Execute(null);
        Assert.False(vm.Forms[0].Seen); Assert.False(vm.Forms[0].Caught);
        vm.ResetCommand.Execute(null); Assert.False(vm.CanUndo);
    }

    [Fact]
    public async Task DeclinedBulkAndCancelPreserveExactSource()
    {
        var save = new SAV9ZA(); var before = save.Zukan.Data.ToArray(); save.State.Edited = false;
        using var vm = new Pokedex9aEditorViewModel(save, Dialog(false).Object);
        await vm.BulkCommand.ExecuteAsync("Complete"); Assert.False(vm.CanUndo);
        vm.Forms[0].Seen = true; vm.CancelCommand.Execute(null); vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray()); Assert.False(save.State.Edited);
    }

    [Fact]
    public async Task ClosingWhileConfirmationIsPendingDiscardsTheLateAnswer()
    {
        var save = Pokedex9aFixtureTests.CreateSave(1);
        var before = save.AllBlocks.Select(block => block.Data.ToArray()).ToArray();
        save.State.Edited = false;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(answer.Task);
        var vm = new Pokedex9aEditorViewModel(save, dialogs.Object) { WholeDex = true };
        var pending = vm.BulkCommand.ExecuteAsync("Complete");
        vm.Dispose();
        answer.SetResult(true);
        await pending;
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanSave);
        Assert.False(save.State.Edited);
        for (int i = 0; i < before.Length; i++) Assert.Equal(before[i], save.AllBlocks[i].Data.ToArray());
    }

    [Fact]
    public void SearchUsesNamesAndClearingRestoresDexOrder()
    {
        using var vm = new Pokedex9aEditorViewModel(new SAV9ZA(), Dialog().Object);
        int count = vm.FilteredSpecies.Count;
        vm.SearchText = GameInfo.Strings.Species[(int)Species.Pikachu];
        Assert.Contains(vm.FilteredSpecies, item => item.Value == (int)Species.Pikachu);
        Assert.True(vm.FilteredSpecies.Count < count); vm.SearchText = string.Empty;
        Assert.Equal(count, vm.FilteredSpecies.Count);
    }

    [Fact]
    public void ExoticMegaDescriptorsFollowRevisionAndPreserveDistinctSlots()
    {
        Assert.Equal(2, Pokedex9aCapabilities.GetMegaFlags((ushort)Species.Charizard, 0).Length);
        Assert.Single(Pokedex9aCapabilities.GetMegaFlags((ushort)Species.Absol, 0));
        Assert.Equal(Dex9aMegaKind.Z, Pokedex9aCapabilities.GetMegaFlags((ushort)Species.Absol, 1)[1].Kind);
        Assert.Equal(3, Pokedex9aCapabilities.GetMegaFlags((ushort)Species.Tatsugiri, 1).Length);
        Assert.Equal(Dex9aMegaKind.MagearnaOriginal, Pokedex9aCapabilities.GetMegaFlags((ushort)Species.Magearna, 1)[1].Kind);
    }
}
