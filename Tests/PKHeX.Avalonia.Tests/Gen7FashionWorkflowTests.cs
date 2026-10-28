using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Gen7FashionWorkflowTests
{
    public static IEnumerable<object[]> Scopes => new[] { GameVersion.SN, GameVersion.US }.SelectMany(game =>
        new[] { 0, 1 }.SelectMany(gender => Enum.GetValues<Gen7FashionMode>().Select(mode => new object[] { game, gender, mode })));

    [Theory] [MemberData(nameof(Scopes))]
    public async Task ConfirmedPayloadStagesUndoesAndRoundtripsOnlyFashion(GameVersion game, int gender, Gen7FashionMode mode)
    {
        var save = Assert.IsAssignableFrom<SAV7>(BlankSaveFile.Get(game)); save.Gender = (byte)gender;
        var before = save.Data.ToArray(); var expected = (SAV7)save.Clone(); Gen7TrainerFashion.Apply(expected, gender, mode);
        var dialogs = new Mock<IDialogService>(); dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        using var vm = new Misc7EditorViewModel(save, dialogs.Object);
        await vm.FashionCommand.ExecuteAsync(mode); Assert.Equal(before, save.Data.ToArray());
        vm.UndoFashionCommand.Execute(null); vm.SaveCommand.Execute(null); Assert.Equal(before, save.Data.ToArray());
        await vm.FashionCommand.ExecuteAsync(mode); vm.SaveCommand.Execute(null);
        Assert.False(vm.HasError); Assert.Equal(expected.Data.ToArray(), save.Data.ToArray());
        var after = save.Data.ToArray(); vm.SaveCommand.Execute(null); Assert.Equal(after, save.Data.ToArray());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ResetOrDisposeDiscardsLateConfirmation(bool close)
    {
        var save = new SAV7USUM(); var before = save.Data.ToArray(); var answer = new TaskCompletionSource<bool>();
        var dialogs = new Mock<IDialogService>(); dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(answer.Task);
        using var vm = new Misc7EditorViewModel(save, dialogs.Object); var pending = vm.FashionCommand.ExecuteAsync(Gen7FashionMode.All);
        if (close) vm.Dispose(); else vm.ResetCommand.Execute(null);
        answer.SetResult(true); await pending; Assert.False(vm.CanUndoFashion); Assert.Equal(before, save.Data.ToArray());
    }

    [Fact]
    public async Task CancelAndUnknownGenderCannotChangeFashionOrOtherDrafts()
    {
        var save = new SAV7SM(); var before = save.Data.ToArray(); var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        using var vm = new Misc7EditorViewModel(save, dialogs.Object); vm.TrainerName = "Draft";
        await vm.FashionCommand.ExecuteAsync(Gen7FashionMode.Legal); Assert.False(vm.CanUndoFashion); Assert.Equal("Draft", vm.TrainerName);
        vm.Gender = 255; await vm.FashionCommand.ExecuteAsync(Gen7FashionMode.All); Assert.Equal(before, save.Data.ToArray());
    }
}
