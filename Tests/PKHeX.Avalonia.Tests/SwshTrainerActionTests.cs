using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class SwshTrainerActionTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LateConfirmationCannotRestoreActionsAfterResetOrClose(bool close)
    {
        var save = Revision(2); var answer = new TaskCompletionSource<bool>();
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(answer.Task);
        using var vm = new Misc8EditorViewModel(save, dialogs.Object);
        var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        var pending = vm.CollectAllDiglettCommand.ExecuteAsync(null);
        if (close) vm.Dispose(); else vm.ResetCommand.Execute(null);
        answer.SetResult(true); await pending; Assert.False(vm.CanUndo);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
    internal static SAV8SWSH Revision(int revision)
    {
        var source = TrainerScBlockSessionTests.LoadPublicSave();
        // Synthetic capability variants: remove only DLC dex allocations from the public CT fixture.
        var blocks = source.AllBlocks.Where(block => revision >= 1 || block.Key != 0x3F936BA9)
            .Where(block => revision >= 2 || block.Key != 0x3C9366F0).ToArray();
        var save = new SAV8SWSH(SwishCrypto.Encrypt(blocks)); Assert.Equal(revision, save.SaveRevision); return save;
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RevisionAwareFieldsHaveNoOpAndCancelSemantics(int revision)
    {
        var save = Revision(revision); var before = save.AllBlocks.ToDictionary(b => b.Key, b => (b.Type, Data: b.Data.ToArray()));
        using var vm = new Misc8EditorViewModel(save); Assert.Equal(revision >= 1, vm.IsIoA);
        Assert.Equal(revision >= 1, vm.TrainerRecords.Any(r => r.Id == 50)); vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        foreach (var b in save.AllBlocks) { Assert.Equal(before[b.Key].Type, b.Type); Assert.Equal(before[b.Key].Data, b.Data.ToArray()); }
        vm.LeagueCardName = "Discard"; vm.X += 2; vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key].Data, b.Data.ToArray());
    }
    [Theory] [InlineData("Fashion")] [InlineData("Appearance")] [InlineData("Diglett")]
    public async Task ConfirmedActionsStageAndUndoWithoutChangingOtherDrafts(string action)
    {
        var save = Revision(2); var before = save.AllBlocks.ToDictionary(b => b.Key, b => (b.Type, Data: b.Data.ToArray()));
        var dialogs = new Mock<IDialogService>(); dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        using var vm = new Misc8EditorViewModel(save, dialogs.Object); vm.LeagueCardName = "Draft";
        if (action == "Fashion") await vm.UnlockAllFashionCommand.ExecuteAsync(null);
        else if (action == "Appearance") await vm.ResetAppearanceCommand.ExecuteAsync(null);
        else await vm.CollectAllDiglettCommand.ExecuteAsync(null);
        foreach (var b in save.AllBlocks) { Assert.Equal(before[b.Key].Type, b.Type); Assert.Equal(before[b.Key].Data, b.Data.ToArray()); }
        vm.UndoCommand.Execute(null); Assert.Equal("Draft", vm.LeagueCardName);
        vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        foreach (var b in save.AllBlocks) { Assert.Equal(before[b.Key].Type, b.Type); Assert.Equal(before[b.Key].Data, b.Data.ToArray()); }
    }
    [Fact]
    public async Task AppearanceConfirmationDiscardsAChangedCharacterScope()
    {
        var save = Revision(2); var before = save.MyStatus.Data.ToArray(); var answer = new TaskCompletionSource<bool>();
        var dialogs = new Mock<IDialogService>(); dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(answer.Task);
        using var vm = new Misc8EditorViewModel(save, dialogs.Object);
        var pending = vm.ResetAppearanceCommand.ExecuteAsync(null); vm.Gender = vm.Gender == 0 ? 1 : 0;
        answer.SetResult(true); await pending; Assert.False(vm.CanUndo); Assert.Equal(before, save.MyStatus.Data.ToArray());
    }
    [Theory] [InlineData("Fashion")] [InlineData("Appearance")] [InlineData("Diglett")]
    public async Task AppliedActionsMatchPublicCoreOperationsWithoutTouchingOtherBlocks(string action)
    {
        var save = Revision(2); var expected = (SAV8SWSH)save.Clone();
        var dialogs = new Mock<IDialogService>(); dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        using var vm = new Misc8EditorViewModel(save, dialogs.Object);
        if (action == "Fashion") { expected.Fashion.UnlockAllLegal(); await vm.UnlockAllFashionCommand.ExecuteAsync(null); }
        else if (action == "Appearance") { expected.MyStatus.ResetAppearance((PlayerSkinColor8)vm.SkinColor); await vm.ResetAppearanceCommand.ExecuteAsync(null); }
        else { expected.UnlockAllDiglett(); await vm.CollectAllDiglettCommand.ExecuteAsync(null); }
        vm.SaveCommand.Execute(null); Assert.False(vm.HasError);
        foreach (var b in save.AllBlocks)
        { var e = expected.Blocks.GetBlock(b.Key); Assert.Equal(e.Type, b.Type); Assert.Equal(e.Data.ToArray(), b.Data.ToArray()); }
    }
}
