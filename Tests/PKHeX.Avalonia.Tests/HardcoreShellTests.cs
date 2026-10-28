using System.Linq;
using System.Threading.Tasks;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Infrastructure.Bank;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Hardcore mode in the shell: tools that edit are closed, slot edits and copies are refused with an
/// explanation, moves still work, a refused save explains itself without writing, and the Settings
/// toggle goes through <see cref="HardcoreMode"/>.
/// </summary>
public sealed class HardcoreShellTests
{
    private static (MainWindowViewModel Vm, HardcoreMode Mode, Mock<IDialogService> Dialogs, Mock<ISaveFileGateway> Gateway)
        Create(SlotService? slotService = null, UndoRedoService? undoRedo = null)
    {
        var settings = new AppSettings();
        var mode = new HardcoreMode(settings);
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var gateway = new Mock<ISaveFileGateway>();
        var vm = new MainWindowViewModel(
            gateway.Object,
            dialogs.Object,
            new Mock<IWindowService>().Object,
            new Mock<ISpriteRenderer>().Object,
            (ISlotService?)slotService ?? new Mock<ISlotService>().Object,
            new Mock<IClipboardService>().Object,
            new Mock<IQrCodeService>().Object,
            UpdateTestDoubles.Coordinator(),
            new Mock<ISaveBackupService>().Object,
            settings,
            new FakeSettingsStore(),
            new Mock<IThemeService>().Object,
            new Mock<IUiDensityService>().Object,
            undoRedo ?? new UndoRedoService(),
            new BoxSelectionService(),
            new LanguageService(),
            new Mock<IAutoLegalityService>().Object,
            new Mock<PKHeX.Application.Abstractions.LiveHex.ILiveHexService>().Object,
            new Mock<ILivingDexService>().Object,
            new Mock<PKHeX.Application.Abstractions.GiftRecords.IGiftRecordProvider>().Object,
            hardcore: mode);
        return (vm, mode, dialogs, gateway);
    }

    private static void Enable(HardcoreMode mode) => mode.SetEnabled(true);

    private static void Restore() => new AppSettings().InitializeCore();

    [Fact]
    public void Tools_ThatEdit_CloseWhileOn_AndReopenWhenOff()
    {
        var (vm, mode, _, _) = Create();
        vm.CurrentSave = new SAV6XY();
        ToolLauncherItem Tool(string id) => vm.ToolLauncherItems.First(i => i.ActionId == id);
        try
        {
            Assert.True(Tool("OpenBatchEditor").CanInvoke());
            Assert.True(Tool("OpenBoxReport").CanInvoke());

            Enable(mode);

            Assert.True(vm.IsHardcore);
            foreach (var id in new[] { "OpenBatchEditor", "OpenBlockEditor", "ImportShowdown", "OpenAutoLegalityMod",
                         "OpenLivingDexGenerator", "LoadBoxes", "OpenDaycare", "OpenRecords", "OpenSuperTraining", "OpenLiveHeX" })
            {
                Assert.False(Tool(id).CanInvoke(), id);
            }
            foreach (var id in new[] { "OpenBoxReport", "OpenLivingDexTracker", "OpenLegalityAudit", "OpenBackupManager",
                         "OpenSaveDiff", "ExportShowdownBox", "DumpBoxes", "OpenLivingDexArrange" })
            {
                Assert.True(Tool(id).CanInvoke(), id);
            }
            Assert.DoesNotContain(vm.FilteredToolLauncherItems, i => i.ActionId == "OpenBatchEditor");

            mode.SetEnabled(false);
            Assert.True(Tool("OpenBatchEditor").CanInvoke());
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Slots_CopyAndSetAreRefused_MoveStillWorks()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        var slotService = new SlotService();
        var undoRedo = new UndoRedoService();
        var (vm, mode, dialogs, _) = Create(slotService, undoRedo);
        vm.CurrentSave = sav;
        undoRedo.Initialize(sav);
        try
        {
            Enable(mode);

            slotService.RequestMove(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), clone: true);
            Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 1).Species);

            slotService.RequestSet(SlotLocation.FromBox(0, 2));
            Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 2).Species);
            dialogs.Verify(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));

            slotService.RequestMove(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), clone: false);
            Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 0).Species);
            Assert.Equal(25, sav.GetBoxSlotAtIndex(0, 1).Species);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task Save_WithAnEdit_ExplainsAndNeverReachesTheGateway()
    {
        var sav = new SAV5B2W2();
        var (vm, mode, dialogs, gateway) = Create();
        vm.CurrentSave = sav;
        mode.OnSaveOpened(sav);
        try
        {
            Enable(mode);
            sav.Money++;

            await vm.SaveFileCommand.ExecuteAsync(null);

            dialogs.Verify(d => d.ShowErrorAsync(It.IsAny<string>(),
                It.Is<string>(m => m == LocalizedStrings.Instance["Hardcore_RefusedSaveData"])), Times.Once);
            gateway.Verify(g => g.SaveFileAsync(It.IsAny<string?>()), Times.Never);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void BoxViewer_HidesEditsAndCopies_WhileOn()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        var mode = new HardcoreMode(new AppSettings());
        var selection = new BoxSelectionService();
        var bulk = new BulkSlotActionsController(sav, new UndoRedoService(), selection, new Mock<IDialogService>().Object,
            null, null, () => null, hardcore: mode);
        var viewer = new BoxViewerViewModel(sav, new Mock<ISpriteRenderer>().Object, selection: selection, bulkActions: bulk, hardcore: mode);
        selection.EnterSelectionMode();
        selection.Toggle(SlotLocation.FromBox(0, 0));
        try
        {
            Assert.True(viewer.IsEditingAllowed);
            Assert.True(viewer.BulkDuplicateCommand.CanExecute(null));

            mode.SetEnabled(true);
            viewer.RefreshHardcore();

            Assert.False(viewer.IsEditingAllowed);
            Assert.False(viewer.BulkDuplicateCommand.CanExecute(null));
            Assert.False(viewer.BulkCopyHereCommand.CanExecute(null));
            Assert.True(viewer.BulkMoveHereCommand.CanExecute(null));
            Assert.True(viewer.BulkReleaseCommand.CanExecute(null));
        }
        finally
        {
            viewer.DetachSelectionEvents();
            Restore();
        }
    }

    [Fact]
    public void Settings_TurnsTheModeOnThroughHardcoreMode()
    {
        var (vm, mode, _, _) = Create();
        var settingsVm = new SettingsViewModel(new AppSettings(), new FakeSettingsStore(),
            new Mock<IThemeService>().Object, new Mock<IUiDensityService>().Object, new LanguageService(),
            UpdateTestDoubles.Coordinator(), hardcore: mode);
        try
        {
            Assert.False(settingsVm.HardcoreEnabled);
            settingsVm.HardcoreEnabled = true;
            settingsVm.SaveCommand.Execute(null);

            Assert.True(mode.IsEnabled);
            Assert.True(vm.IsHardcore);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Bank_ArchiveImportClosesWhileOn()
    {
        var mode = new HardcoreMode(new AppSettings());
        var paths = new FakeAppPaths();
        var bank = new BankViewModel(new FileBankService(paths), new Mock<ISpriteRenderer>().Object, hardcore: mode);
        try
        {
            Assert.True(bank.CanImportArchive);
            var raised = false;
            bank.ImportArchiveCommand.CanExecuteChanged += (_, _) => raised = true;

            mode.SetEnabled(true);

            Assert.True(raised);
            Assert.False(bank.CanImportArchive);
            Assert.False(bank.ImportArchiveCommand.CanExecute(null));
        }
        finally
        {
            Restore();
            try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(paths.DataDirectory)!, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
