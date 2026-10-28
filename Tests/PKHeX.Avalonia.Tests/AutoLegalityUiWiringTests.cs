using Moq;
using PKHeX.Avalonia.Services;
using PKHeX.Presentation.ViewModels;
using PKHeX.Core;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Wiring tests for the two UI entry points onto <see cref="IAutoLegalityService.TryLegalizeExisting"/>
/// (issue #89 Gap A): the single-mon Legality dialog's "Legalize" button
/// (<see cref="LegalityViewModel"/>/<see cref="PokemonEditorViewModel"/>), and the box/party slot
/// context menu's "Legalize" entry (<see cref="BoxViewerViewModel"/>/<see cref="PartyViewerViewModel"/>).
/// These tests mock <see cref="IAutoLegalityService"/> — the engine itself is covered by
/// <see cref="AutoLegalityServiceTests"/> — and check only that the commands exist, are gated
/// correctly, and invoke/consume the service as expected.
/// </summary>
public class AutoLegalityUiWiringTests(ITestOutputHelper output)
{
    private static Mock<ISpriteRenderer> SpriteMock() => new();

    // -----------------------------------------------------------------------
    // LegalityViewModel: command gating
    // -----------------------------------------------------------------------

    [Fact]
    public void LegalityViewModel_ReadOnlyConstructor_OffersNoLegalizeAction()
    {
        var vm = new LegalityViewModel("Legal\nNo issues found.");

        Assert.False(vm.CanOfferLegalize);
        Assert.False(vm.CanLegalize);
        Assert.False(vm.LegalizeCommand.CanExecute(null));
    }

    [Fact]
    public void LegalityViewModel_IllegalPokemonWithService_OffersLegalizeAction()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var pk = new PK4 { Species = 1 };
        var service = new Mock<IAutoLegalityService>();

        var vm = new LegalityViewModel("illegal report", isLegal: false, sav, pk, service.Object);

        Assert.True(vm.CanOfferLegalize);
        Assert.True(vm.CanLegalize);
        Assert.True(vm.LegalizeCommand.CanExecute(null));
    }

    [Fact]
    public void LegalityViewModel_AlreadyLegalPokemon_DoesNotOfferLegalizeAction()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var pk = new PK4 { Species = 1 };
        var service = new Mock<IAutoLegalityService>();

        var vm = new LegalityViewModel("legal report", isLegal: true, sav, pk, service.Object);

        Assert.True(vm.CanOfferLegalize); // a service/save/pk were supplied...
        Assert.False(vm.CanLegalize);     // ...but it's already legal, so there's nothing to do
        Assert.False(vm.LegalizeCommand.CanExecute(null));
    }

    [Fact]
    public async Task LegalityViewModel_LegalizeAsync_OnSuccess_RaisesLegalizedAndRequestsClose()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var illegal = new PK4 { Species = 1 };
        var legalized = new PK4 { Species = 1, HeldItem = 5 };
        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, illegal, null))
            .Returns(AutoLegalityResult.Ok(legalized, "Legal\nNo issues found."));

        var vm = new LegalityViewModel("illegal report", isLegal: false, sav, illegal, service.Object);

        PKM? raised = null;
        vm.Legalized += pk => raised = pk;
        var closed = false;
        vm.CloseRequested += () => closed = true;

        await vm.LegalizeCommand.ExecuteAsync(null);

        service.Verify(s => s.TryLegalizeExisting(sav, illegal, null), Times.Once);
        Assert.Same(legalized, raised);
        Assert.True(closed);
    }

    [Fact]
    public async Task LegalityViewModel_LegalizeAsync_OnFailure_SetsStatusMessage_AndDoesNotClose()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var illegal = new PK4 { Species = 1 };
        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, illegal, null))
            .Returns(AutoLegalityResult.Failure(LegalizationStatus.Failed, null, "no legal encounter found"));

        var vm = new LegalityViewModel("illegal report", isLegal: false, sav, illegal, service.Object);

        var raised = false;
        vm.Legalized += _ => raised = true;
        var closed = false;
        vm.CloseRequested += () => closed = true;

        await vm.LegalizeCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(closed);
        Assert.Contains("no legal encounter found", vm.StatusMessage);
    }

    // -----------------------------------------------------------------------
    // PokemonEditorViewModel: the Legality dialog's "Legalize" action adopts the result
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ShowLegalityCommand_WhenDialogLegalizeSucceeds_LoadsResultIntoEditor()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var original = new PK4 { Species = 399 }; // Bidoof
        var legalized = new PK4 { Species = 399, HeldItem = 5 };

        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()))
            .Returns(AutoLegalityResult.Ok(legalized, "Legal\nNo issues found."));

        var windowService = new Mock<IWindowService>();
        windowService
            .Setup(w => w.ShowDialogAsync(It.IsAny<object>(), It.IsAny<string>()))
            .Returns<object, string>(async (vm, _) =>
            {
                var legalityVm = Assert.IsType<LegalityViewModel>(vm);
                Assert.True(legalityVm.CanLegalize);
                await legalityVm.LegalizeCommand.ExecuteAsync(null);
            });

        var editor = new PokemonEditorViewModel(
            original, sav, SpriteMock().Object, Mock.Of<IDialogService>(), windowService.Object,
            haXMode: false, autoLegalityService: service.Object);

        await editor.ShowLegalityCommand.ExecuteAsync(null);

        Assert.Equal(5, editor.HeldItem);
        output.WriteLine("Editor adopted the legalized Pokémon's HeldItem via LoadPKM.");
    }

    [Fact]
    public async Task ShowLegalityCommand_InHaXMode_DoesNotOfferLegalizeAction()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var original = new PK4 { Species = 399 };
        var service = new Mock<IAutoLegalityService>();

        var windowService = new Mock<IWindowService>();
        windowService
            .Setup(w => w.ShowDialogAsync(It.IsAny<object>(), It.IsAny<string>()))
            .Returns<object, string>((vm, _) =>
            {
                var legalityVm = Assert.IsType<LegalityViewModel>(vm);
                Assert.False(legalityVm.CanOfferLegalize);
                return Task.CompletedTask;
            });

        var editor = new PokemonEditorViewModel(
            original, sav, SpriteMock().Object, Mock.Of<IDialogService>(), windowService.Object,
            haXMode: true, autoLegalityService: service.Object);

        await editor.ShowLegalityCommand.ExecuteAsync(null);

        service.Verify(s => s.TryLegalizeExisting(It.IsAny<SaveFile>(), It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // BoxViewerViewModel: context menu "Legalize" entry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BoxViewer_LegalizeSlotCommand_OnSuccess_WritesResultBackToSlot()
    {
        var sav = new SAV8SWSH();
        sav.SetBoxSlotAtIndex(new PK8 { Species = 1 }, 0);

        var legalized = new PK8 { Species = 1, HeldItem = 5 };
        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), null))
            .Returns(AutoLegalityResult.Ok(legalized, "Legal\nNo issues found."));

        var vm = new BoxViewerViewModel(sav, SpriteMock().Object, autoLegalityService: service.Object);

        await vm.LegalizeSlotCommand.ExecuteAsync(vm.Slots[0]);

        service.Verify(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), null), Times.Once);
        Assert.Equal(5, vm.GetSlotPKM(0).HeldItem);
    }

    [Fact]
    public async Task BoxViewer_LegalizeSlotCommand_OnEmptySlot_NeverCallsService()
    {
        var sav = new SAV8SWSH();
        var service = new Mock<IAutoLegalityService>();
        var vm = new BoxViewerViewModel(sav, SpriteMock().Object, autoLegalityService: service.Object);

        Assert.True(vm.Slots[0].IsEmpty);
        await vm.LegalizeSlotCommand.ExecuteAsync(vm.Slots[0]);

        service.Verify(s => s.TryLegalizeExisting(It.IsAny<SaveFile>(), It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()), Times.Never);
    }

    [Fact]
    public async Task BoxViewer_LegalizeSlotCommand_OnFailure_ShowsErrorDialog_DoesNotMutateSlot()
    {
        var sav = new SAV8SWSH();
        sav.SetBoxSlotAtIndex(new PK8 { Species = 1 }, 0);

        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), null))
            .Returns(AutoLegalityResult.Failure(LegalizationStatus.Failed, null, "no legal encounter found"));

        var dialogService = new Mock<IDialogService>();
        var vm = new BoxViewerViewModel(sav, SpriteMock().Object, dialogService: dialogService.Object, autoLegalityService: service.Object);

        await vm.LegalizeSlotCommand.ExecuteAsync(vm.Slots[0]);

        dialogService.Verify(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Equal(1, vm.GetSlotPKM(0).Species);
    }

    // -----------------------------------------------------------------------
    // PartyViewerViewModel: context menu "Legalize" entry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PartyViewer_LegalizeSlotCommand_OnSuccess_WritesResultBackToSlot()
    {
        var sav = new SAV8SWSH();
        sav.SetPartySlotAtIndex(new PK8 { Species = 1 }, 0);

        var legalized = new PK8 { Species = 1, HeldItem = 5 };
        var service = new Mock<IAutoLegalityService>();
        service
            .Setup(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), null))
            .Returns(AutoLegalityResult.Ok(legalized, "Legal\nNo issues found."));

        var vm = new PartyViewerViewModel(sav, SpriteMock().Object, autoLegalityService: service.Object);

        await vm.LegalizeSlotCommand.ExecuteAsync(vm.Slots[0]);

        service.Verify(s => s.TryLegalizeExisting(sav, It.IsAny<PKM>(), null), Times.Once);
        Assert.Equal(5, vm.GetSlotPKM(0).HeldItem);
    }

    [Fact]
    public async Task PartyViewer_LegalizeSlotCommand_OnEmptySlot_NeverCallsService()
    {
        var sav = new SAV8SWSH();
        var service = new Mock<IAutoLegalityService>();
        var vm = new PartyViewerViewModel(sav, SpriteMock().Object, autoLegalityService: service.Object);

        Assert.True(vm.Slots[0].IsEmpty);
        await vm.LegalizeSlotCommand.ExecuteAsync(vm.Slots[0]);

        service.Verify(s => s.TryLegalizeExisting(It.IsAny<SaveFile>(), It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()), Times.Never);
    }
}
