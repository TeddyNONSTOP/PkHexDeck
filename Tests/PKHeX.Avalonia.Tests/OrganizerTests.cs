using Avalonia.Headless.XUnit;
using Moq;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Presentation-level coverage for the Organizer (per-slot multi-select + bulk actions):
/// <see cref="BulkSlotActionsController"/>'s confirm → restore point → one undo transaction →
/// report pipeline, and the box/party viewer commands and click routing that drive it.
/// </summary>
public class OrganizerTests
{
    private sealed class Harness
    {
        public SAV6XY Save { get; } = new();
        public UndoRedoService UndoRedo { get; } = new();
        public BoxSelectionService Selection { get; } = new();
        public Mock<IDialogService> Dialogs { get; } = new();
        public Mock<ISaveBackupService> Backups { get; } = new();
        public List<string> Messages { get; } = [];
        public List<string> Confirmations { get; } = [];
        public bool Confirm { get; set; } = true;
        public BulkSlotActionsController Controller { get; }
        public int Completed { get; private set; }

        public Harness(string? path = "/tmp/organizer-test.sav")
        {
            UndoRedo.Initialize(Save);
            Dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string _, string message, string _, string _) =>
                {
                    Confirmations.Add(message);
                    return Confirm;
                });
            Dialogs.Setup(d => d.ShowInformationAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string _, string message) => Messages.Add(message))
                .Returns(Task.CompletedTask);
            Dialogs.Setup(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string _, string message) => Messages.Add("ERROR: " + message))
                .Returns(Task.CompletedTask);
            Controller = new BulkSlotActionsController(Save, UndoRedo, Selection, Dialogs.Object, Backups.Object, new AppSettings(), () => path);
            Controller.Completed += () => Completed++;
        }

        public void Put(int box, int slot, ushort species) => Save.SetBoxSlotAtIndex(new PK6 { Species = species }, box, slot);
        public ushort At(int box, int slot) => Save.GetBoxSlotAtIndex(box, slot).Species;
        public void Mark(params SlotLocation[] locations)
        {
            foreach (var location in locations)
                Selection.Toggle(location);
        }

        public BoxViewerViewModel CreateBoxViewer() =>
            new(Save, new Mock<ISpriteRenderer>().Object, selection: Selection, bulkActions: Controller);

        public PartyViewerViewModel CreatePartyViewer() =>
            new(Save, new Mock<ISpriteRenderer>().Object, selection: Selection, bulkActions: Controller);
    }

    private static string F(string key, params object[] args) => LocalizedStrings.Instance.Format(key, args);

    // ---------------------------------------------------------------- controller

    [Fact]
    public async Task BulkMove_IsOneUndoStep_AndUndoRestoresEverySlot()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Put(0, 1, 4);
        h.Put(0, 2, 7);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 2));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(1, 0), clone: false, "Box 2");

        Assert.Equal([1, 4, 7], new[] { h.At(1, 0), h.At(1, 1), h.At(1, 2) }.Select(x => (int)x));
        Assert.Equal(0, h.At(0, 0));
        Assert.Equal(1, h.UndoRedo.ChangeCount);
        Assert.True(h.Save.State.Edited);
        Assert.Equal(1, h.Completed);
        Assert.Equal(0, h.Selection.Count);
        Assert.Single(h.Confirmations);

        h.UndoRedo.Undo();

        Assert.Equal([1, 4, 7], new[] { h.At(0, 0), h.At(0, 1), h.At(0, 2) }.Select(x => (int)x));
        Assert.Equal(0, h.At(1, 0));
        Assert.Equal(0, h.At(1, 1));
        Assert.Equal(0, h.At(1, 2));
        Assert.False(h.UndoRedo.CanUndo);

        h.UndoRedo.Redo();
        Assert.Equal(7, h.At(1, 2));
        Assert.Equal(0, h.At(0, 2));
    }

    [Fact]
    public async Task BulkMove_IntoFullBox_ReportsSkipped_AndKeepsTheLeftoversMarked()
    {
        var h = new Harness();
        for (var slot = 0; slot < h.Save.BoxSlotCount - 1; slot++)
            h.Put(1, slot, 10);
        h.Put(0, 0, 1);
        h.Put(0, 1, 4);
        h.Put(0, 2, 7);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 2));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(1, 0), clone: false, "Box 2");

        Assert.Equal(1, h.At(1, h.Save.BoxSlotCount - 1));
        Assert.Equal(10, h.At(1, 0)); // never overwritten
        Assert.Equal(4, h.At(0, 1));
        Assert.Equal(7, h.At(0, 2));
        var report = Assert.Single(h.Messages);
        Assert.Contains(F("Organizer_ResultMoved", 1), report);
        Assert.Contains(F("Organizer_SkippedNoRoom", 2), report);
        Assert.Equal([SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 2)], h.Selection.Snapshot());
    }

    [Fact]
    public async Task BulkMove_WithNoRoomAtAll_ChangesNothing_AndAsksNoConfirmation()
    {
        var h = new Harness();
        for (var slot = 0; slot < h.Save.BoxSlotCount; slot++)
            h.Put(1, slot, 10);
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(1, 0), clone: false, "Box 2");

        Assert.Empty(h.Confirmations);
        Assert.Equal(0, h.UndoRedo.ChangeCount);
        Assert.Contains(F("Organizer_SkippedNoRoom", 1), Assert.Single(h.Messages));
    }

    [Fact]
    public async Task BulkAction_WithASingleMark_Works()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(4, 0), clone: true, "Box 5");

        Assert.Equal(1, h.At(4, 0));
        Assert.Equal(1, h.At(0, 0));
    }

    [Fact]
    public async Task CancellingTheConfirmation_ChangesNothing_AndMakesNoRestorePoint()
    {
        var h = new Harness { Confirm = false };
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));

        await h.Controller.ReleaseMarkedAsync();

        Assert.Equal(1, h.At(0, 0));
        Assert.Equal(0, h.UndoRedo.ChangeCount);
        Assert.Equal(1, h.Selection.Count);
        h.Backups.Verify(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmedBulkAction_CreatesExactlyOneRestorePointOfThePreWriteSave()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Put(0, 1, 4);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1));
        byte[]? snapshot = null;
        h.Backups.Setup(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Callback((string _, byte[] data, int _) => snapshot = data)
            .Returns((SaveBackupEntry?)null);

        await h.Controller.ReleaseMarkedAsync();

        h.Backups.Verify(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()), Times.Once);
        Assert.NotNull(snapshot);
        var restored = new SAV6XY(snapshot!);
        Assert.Equal(1, restored.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(0, h.At(0, 0));
    }

    [Fact]
    public async Task BulkRelease_IsOneUndoStep()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Put(3, 7, 4);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(3, 7));

        await h.Controller.ReleaseMarkedAsync();
        Assert.Equal(0, h.At(0, 0));
        Assert.Equal(0, h.At(3, 7));
        Assert.Equal(1, h.UndoRedo.ChangeCount);
        Assert.Contains(F("Organizer_ResultReleased", 2), Assert.Single(h.Messages));

        h.UndoRedo.Undo();
        Assert.Equal(1, h.At(0, 0));
        Assert.Equal(4, h.At(3, 7));
    }

    [Fact]
    public async Task BulkRelease_ExcludesPartyMarks_AndReportsThem()
    {
        var h = new Harness();
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 25 }, 0);
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromParty(0), SlotLocation.FromBox(0, 0));

        await h.Controller.ReleaseMarkedAsync();

        Assert.Equal(25, h.Save.GetPartySlotAtIndex(0).Species);
        Assert.Equal(1, h.Save.PartyCount);
        Assert.Equal(0, h.At(0, 0));
        Assert.Contains(F("Organizer_ExcludedParty", 1), Assert.Single(h.Confirmations));
        Assert.Contains(F("Organizer_ExcludedParty", 1), Assert.Single(h.Messages));
    }

    [Fact]
    public async Task BulkRelease_OfOnlyPartyMarks_ReleasesNothing()
    {
        var h = new Harness();
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 25 }, 0);
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 26 }, 1);
        h.Mark(SlotLocation.FromParty(0), SlotLocation.FromParty(1));

        await h.Controller.ReleaseMarkedAsync();

        Assert.Equal(2, h.Save.PartyCount);
        Assert.Empty(h.Confirmations);
        Assert.Equal(LocalizedStrings.Instance["Organizer_NothingToReleaseMessage"], Assert.Single(h.Messages));
    }

    [Fact]
    public async Task BulkRelease_RefusesTheWholeBatch_WhenAMarkedSlotIsLocked()
    {
        var sav = new SAV7SM();
        sav.BoxLayout.ClearBattleTeams();
        sav.BoxLayout.TeamSlots[0] = 0;
        sav.BoxLayout.SetIsTeamLocked(0, true);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 1 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 4 }, 0, 5);
        Assert.True(sav.IsBoxSlotLocked(0, 0));

        var undo = new UndoRedoService();
        undo.Initialize(sav);
        var selection = new BoxSelectionService();
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 5));
        var messages = new List<string>();
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowInformationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => messages.Add(message))
            .Returns(Task.CompletedTask);
        var controller = new BulkSlotActionsController(sav, undo, selection, dialogs.Object, null, null, () => null);

        await controller.ReleaseMarkedAsync();

        Assert.Equal(1, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(4, sav.GetBoxSlotAtIndex(0, 5).Species); // not partially released
        Assert.Equal(0, undo.ChangeCount);
        Assert.Equal(F("Organizer_ReleaseRefusedMessage", 1), Assert.Single(messages));
        dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public async Task BulkMovePartyToBox_CompactsParty_AndUndoRestoresIt()
    {
        var h = new Harness();
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 2 }, 1);
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 3 }, 2);
        h.Mark(SlotLocation.FromParty(0), SlotLocation.FromParty(2));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(0, 0), clone: false, "Box 1");

        Assert.Equal(1, h.Save.PartyCount);
        Assert.Equal(2, h.Save.GetPartySlotAtIndex(0).Species);
        Assert.Equal(1, h.At(0, 0));
        Assert.Equal(3, h.At(0, 1));

        h.UndoRedo.Undo();
        Assert.Equal(3, h.Save.PartyCount);
        Assert.Equal([1, 2, 3], Enumerable.Range(0, 3).Select(i => (int)h.Save.GetPartySlotAtIndex(i).Species));
        Assert.Equal(0, h.At(0, 0));
        Assert.Equal(0, h.At(0, 1));
    }

    [Fact]
    public async Task BulkMove_ThatWouldEmptyTheParty_IsRefused()
    {
        var h = new Harness();
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        h.Mark(SlotLocation.FromParty(0));

        await h.Controller.TransferMarkedAsync(SlotLocation.FromBox(0, 0), clone: false, "Box 1");

        Assert.Equal(1, h.Save.PartyCount);
        Assert.Equal(0, h.At(0, 0));
        Assert.Empty(h.Confirmations);
        Assert.Equal(LocalizedStrings.Instance["Organizer_WouldEmptyPartyMessage"], Assert.Single(h.Messages));
    }

    [Fact]
    public async Task BulkDuplicate_ClonesIntoEachSourcesOwnBox()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Put(2, 0, 4);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(2, 0));

        await h.Controller.DuplicateMarkedAsync();

        Assert.Equal(1, h.At(0, 1));
        Assert.Equal(4, h.At(2, 1));
        Assert.Equal(1, h.UndoRedo.ChangeCount);
    }

    [Fact]
    public async Task BulkExport_WritesFiles_KeepsMarks_AndDoesNotTouchTheSave()
    {
        var h = new Harness();
        h.Put(0, 0, 25);
        h.Put(0, 3, 26);
        h.Mark(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 3));
        var folder = Directory.CreateTempSubdirectory("pkhex-organizer-").FullName;
        try
        {
            h.Dialogs.Setup(d => d.OpenFolderAsync(It.IsAny<string>())).ReturnsAsync(folder);

            await h.Controller.ExportMarkedAsync();

            Assert.Equal(2, Directory.GetFiles(folder).Length);
            Assert.Empty(h.Confirmations);
            Assert.Equal(2, h.Selection.Count);
            Assert.Equal(0, h.UndoRedo.ChangeCount);
            Assert.False(h.Save.State.Edited);
            Assert.Equal(F("Organizer_ResultExported", 2, folder), Assert.Single(h.Messages));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RetiredController_DoesNothing()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));
        h.Controller.Retire();

        await h.Controller.ReleaseMarkedAsync();

        Assert.Equal(1, h.At(0, 0));
        Assert.Empty(h.Confirmations);
    }

    [Fact]
    public async Task SaveSwitchDuringTheConfirmation_AbortsTheWrite()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));
        h.Dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                h.Controller.Retire(); // what MainWindowViewModel does on a save change
                return true;
            });

        await h.Controller.ReleaseMarkedAsync();

        Assert.Equal(1, h.At(0, 0));
        Assert.Equal(0, h.UndoRedo.ChangeCount);
    }

    // ---------------------------------------------------------------- viewer view-models

    [Fact]
    public void BoxViewer_BulkCommands_NeedAtLeastOneMark()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        var vm = h.CreateBoxViewer();

        Assert.False(vm.BulkReleaseCommand.CanExecute(null));
        Assert.False(vm.ClearMarksCommand.CanExecute(null));

        h.Selection.EnterSelectionMode();
        vm.SelectSlotByClickCommand.Execute(vm.Slots[0]);

        Assert.Equal(1, vm.MarkedCount);
        Assert.True(vm.HasMarks);
        Assert.True(vm.BulkReleaseCommand.CanExecute(null));
        Assert.True(vm.BulkMoveHereCommand.CanExecute(null));
        Assert.True(vm.BulkMoveToPartyCommand.CanExecute(null));
        Assert.True(vm.ClearMarksCommand.CanExecute(null));
        Assert.Equal(F("Organizer_MarkedCount", 1), vm.MarkedCountText);
    }

    [Fact]
    public void BoxViewer_SelectionModeClick_TogglesTheMark_OutsideSelectionModeItOnlySelects()
    {
        var h = new Harness();
        h.Put(0, 4, 1);
        var vm = h.CreateBoxViewer();

        vm.SelectSlotByClickCommand.Execute(vm.Slots[4]);
        Assert.Equal(4, vm.SelectedIndex);
        Assert.False(vm.Slots[4].IsMarked);

        vm.ToggleSelectionModeCommand.Execute(null);
        Assert.True(vm.IsSelectionModeEnabled);

        vm.SelectSlotByClickCommand.Execute(vm.Slots[4]);
        Assert.True(vm.Slots[4].IsMarked);
        Assert.True(h.Selection.IsMarked(SlotLocation.FromBox(0, 4)));

        vm.SelectSlotByClickCommand.Execute(vm.Slots[4]);
        Assert.False(vm.Slots[4].IsMarked);
    }

    [Fact]
    public void BoxViewer_SelectionModeClickOnEmptySlot_DoesNotMarkIt()
    {
        var h = new Harness();
        var vm = h.CreateBoxViewer();
        h.Selection.EnterSelectionMode();

        vm.SelectSlotByClickCommand.Execute(vm.Slots[2]);

        Assert.Equal(0, h.Selection.Count);
        Assert.Equal(2, vm.SelectedIndex);
    }

    [Fact]
    public void BoxViewer_ActivateInSelectionMode_TogglesInsteadOfLoadingTheSlot()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        var vm = h.CreateBoxViewer();
        var activated = 0;
        vm.SlotActivated += (_, _) => activated++;
        vm.SelectedIndex = 0;

        h.Selection.EnterSelectionMode();
        vm.ActivateSlotCommand.Execute(null);

        Assert.Equal(0, activated);
        Assert.True(vm.Slots[0].IsMarked);

        h.Selection.ExitSelectionMode();
        vm.ActivateSlotCommand.Execute(null);
        Assert.Equal(1, activated);
        Assert.False(vm.Slots[0].IsMarked);
    }

    [Fact]
    public void BoxViewer_SelectAllInBox_AndMarksFollowBoxNavigation()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        h.Put(0, 9, 4);
        h.Put(1, 0, 7);
        var vm = h.CreateBoxViewer();
        Assert.False(vm.SelectAllInBoxCommand.CanExecute(null)); // only in selection mode

        vm.ToggleSelectionModeCommand.Execute(null);
        vm.SelectAllInBoxCommand.Execute(null);

        Assert.Equal(2, vm.MarkedCount);
        Assert.True(vm.Slots[0].IsMarked);
        Assert.True(vm.Slots[9].IsMarked);

        vm.NextBoxCommand.Execute(null);
        Assert.All(vm.Slots, slot => Assert.False(slot.IsMarked));
        vm.PreviousBoxCommand.Execute(null);
        Assert.True(vm.Slots[9].IsMarked);

        vm.ClearMarksCommand.Execute(null);
        Assert.Equal(0, vm.MarkedCount);
        Assert.True(vm.IsSelectionModeEnabled);
    }

    [Fact]
    public async Task BoxViewer_BulkMoveHere_MovesMarksFromOtherBoxesIntoTheCurrentBox()
    {
        var h = new Harness();
        h.Put(2, 0, 1);
        h.Put(3, 0, 4);
        h.Mark(SlotLocation.FromBox(2, 0), SlotLocation.FromBox(3, 0));
        var vm = h.CreateBoxViewer();
        Assert.Equal(0, vm.CurrentBox);

        await vm.BulkMoveHereCommand.ExecuteAsync(null);

        Assert.Equal(1, h.At(0, 0));
        Assert.Equal(4, h.At(0, 1));
        Assert.Contains(vm.BoxName, Assert.Single(h.Confirmations));
    }

    [Fact]
    public async Task PartyViewer_MarksPartySlots_AndBulkMoveHereFillsTheParty()
    {
        var h = new Harness();
        h.Save.SetPartySlotAtIndex(new PK6 { Species = 25 }, 0);
        h.Put(0, 0, 1);
        h.Mark(SlotLocation.FromBox(0, 0));
        var party = h.CreatePartyViewer();
        h.Selection.EnterSelectionMode();

        party.SelectSlotByClickCommand.Execute(party.Slots[0]);
        Assert.True(party.Slots[0].IsMarked);
        Assert.Equal(2, party.MarkedCount);

        party.SelectSlotByClickCommand.Execute(party.Slots[0]); // unmark the party member again
        await party.BulkMoveHereCommand.ExecuteAsync(null);

        Assert.Equal(2, h.Save.PartyCount);
        Assert.Equal(1, h.Save.GetPartySlotAtIndex(1).Species);
        Assert.Equal(0, h.At(0, 0));
    }

    [Fact]
    public void DetachedViewer_NoLongerTracksTheSharedSelection()
    {
        var h = new Harness();
        h.Put(0, 0, 1);
        var vm = h.CreateBoxViewer();
        vm.DetachSelectionEvents();

        h.Selection.Toggle(SlotLocation.FromBox(0, 0));

        Assert.False(vm.Slots[0].IsMarked);
    }

    [AvaloniaFact]
    public void SaveSwitch_ClearsMarksAndSelectionMode_AndRetiresTheOldViewers()
    {
        var gateway = new Mock<ISaveFileGateway>();
        var selection = new BoxSelectionService();
        var vm = new MainWindowViewModel(
            gateway.Object,
            new Mock<IDialogService>().Object,
            new Mock<IWindowService>().Object,
            new Mock<ISpriteRenderer>().Object,
            new SlotService(),
            new Mock<IClipboardService>().Object,
            new Mock<IQrCodeService>().Object,
            UpdateTestDoubles.Coordinator(),
            new Mock<ISaveBackupService>().Object,
            new AppSettings(),
            new FakeSettingsStore(),
            new Mock<IThemeService>().Object,
            new Mock<IUiDensityService>().Object,
            new UndoRedoService(),
            selection,
            new LanguageService(),
            new Mock<IAutoLegalityService>().Object,
            new Mock<PKHeX.Application.Abstractions.LiveHex.ILiveHexService>().Object,
            new Mock<ILivingDexService>().Object,
            new Mock<PKHeX.Application.Abstractions.GiftRecords.IGiftRecordProvider>().Object);

        var first = new SAV6XY();
        first.SetBoxSlotAtIndex(new PK6 { Species = 1 }, 0, 0);
        gateway.Raise(m => m.SaveFileChanged += null, first);
        var oldBox = vm.BoxViewer!;
        Assert.True(oldBox.SupportsOrganizer);
        oldBox.ToggleSelectionModeCommand.Execute(null);
        oldBox.SelectSlotByClickCommand.Execute(oldBox.Slots[0]);
        Assert.Equal(1, selection.Count);

        gateway.Raise(m => m.SaveFileChanged += null, new SAV6XY());

        Assert.Equal(0, selection.Count);
        Assert.False(selection.IsSelectionModeEnabled);
        Assert.NotSame(oldBox, vm.BoxViewer);
        Assert.False(vm.BoxViewer!.IsSelectionModeEnabled);
        Assert.False(vm.BoxViewer.HasMarks);

        // The discarded viewer no longer listens to the shared singleton.
        var oldNotifications = 0;
        oldBox.PropertyChanged += (_, _) => oldNotifications++;
        selection.Toggle(SlotLocation.FromBox(0, 0));
        Assert.Equal(0, oldNotifications);
    }
}
