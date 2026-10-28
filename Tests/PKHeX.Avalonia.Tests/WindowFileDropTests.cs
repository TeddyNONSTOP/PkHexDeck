using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// The window-level OS file drop (a save or Pokémon file dragged from the file manager onto any
/// part of the shell that isn't a slot or the editor panel): dropped save files must open through
/// the same path as File &gt; Open, and dropped entity files must load into the current editor.
/// This pins the ViewModel half of the pipeline; the routed-event plumbing lives in the view.
/// </summary>
public class WindowFileDropTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pkhex-window-drop-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private static MainWindowViewModel CreateViewModel(
        FakeGateway gateway, Mock<IDialogService>? dialogs = null) =>
        new(
            gateway,
            (dialogs ?? new Mock<IDialogService>()).Object,
            new Mock<IWindowService>().Object,
            new Mock<ISpriteRenderer>().Object,
            new Mock<ISlotService>().Object,
            new Mock<IClipboardService>().Object,
            new Mock<IQrCodeService>().Object,
            UpdateTestDoubles.Coordinator(),
            new Mock<ISaveBackupService>().Object,
            new AppSettings(),
            new FakeSettingsStore(),
            new Mock<IThemeService>().Object,
            new Mock<IUiDensityService>().Object,
            new UndoRedoService(),
            new BoxSelectionService(),
            new LanguageService(),
            new Mock<IAutoLegalityService>().Object,
            new Mock<PKHeX.Application.Abstractions.LiveHex.ILiveHexService>().Object,
            new Mock<ILivingDexService>().Object,
            new Mock<PKHeX.Application.Abstractions.GiftRecords.IGiftRecordProvider>().Object);

    // B2 blank saves round-trip detection from disk without needing a real dump (same trick as
    // OsDragDropTests); SAV9SV gives a modern editor pipeline for entity drops.
    private string WriteSaveFile()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "main.sav");
        File.WriteAllBytes(path, BlankSaveFile.Get(GameVersion.B2).Write().ToArray());
        return path;
    }

    private string WriteEntityFile(SaveFile sav, ushort species)
    {
        Directory.CreateDirectory(_dir);
        var pk = (PK9)sav.BlankPKM;
        pk.Species = species;
        pk.RefreshChecksum();
        var path = Path.Combine(_dir, $"000{species}.{pk.Extension}");
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task DroppedSaveFile_OpensThroughTheSaveGateway()
    {
        var gateway = new FakeGateway(loadResult: true);
        var vm = CreateViewModel(gateway);
        var savePath = WriteSaveFile();

        await vm.HandleWindowFileDropAsync([savePath]);

        Assert.Equal(savePath, gateway.LastLoadPath);
    }

    [Fact]
    public async Task DroppedSaveFile_ThatFailsToLoad_ShowsError()
    {
        var dialogs = new Mock<IDialogService>();
        var gateway = new FakeGateway(loadResult: false);
        var vm = CreateViewModel(gateway, dialogs);
        var savePath = WriteSaveFile();

        await vm.HandleWindowFileDropAsync([savePath]);

        dialogs.Verify(d => d.ShowErrorAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DroppedSaveFile_WinsOverOtherFiles()
    {
        var gateway = new FakeGateway(loadResult: true);
        var vm = CreateViewModel(gateway);
        var savePath = WriteSaveFile();
        var junk = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(junk, "not a pokemon");

        await vm.HandleWindowFileDropAsync([junk, savePath]);

        Assert.Equal(savePath, gateway.LastLoadPath);
    }

    [Fact]
    public async Task DroppedEntityFile_LoadsIntoCurrentEditor()
    {
        var gateway = new FakeGateway(loadResult: true);
        var vm = CreateViewModel(gateway);
        var sav = new SAV9SV();
        gateway.OpenLoadedSave(sav, "main.sav");
        Assert.NotNull(vm.CurrentPokemonEditor);

        var entityPath = WriteEntityFile(sav, 25);
        await vm.HandleWindowFileDropAsync([entityPath]);

        Assert.NotNull(vm.CurrentPokemonEditor);
        Assert.Equal((ushort)25, vm.CurrentPokemonEditor.PreparePKM().Species);
    }

    [Fact]
    public async Task DroppedUnsupportedFiles_WithNoSaveOpen_DoNothingQuietly()
    {
        var dialogs = new Mock<IDialogService>();
        var gateway = new FakeGateway(loadResult: true);
        var vm = CreateViewModel(gateway, dialogs);
        var junk = Path.Combine(_dir, "notes.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(junk, "not a pokemon");

        await vm.HandleWindowFileDropAsync([junk]);

        Assert.Null(gateway.LastLoadPath);
        dialogs.Verify(d => d.ShowErrorAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DroppedUnsupportedFiles_WithSaveOpen_ReportNoSupportedFile()
    {
        var dialogs = new Mock<IDialogService>();
        var gateway = new FakeGateway(loadResult: true);
        var vm = CreateViewModel(gateway, dialogs);
        gateway.OpenLoadedSave(new SAV9SV(), "main.sav");
        var junk = Path.Combine(_dir, "notes.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(junk, "not a pokemon");

        await vm.HandleWindowFileDropAsync([junk]);

        dialogs.Verify(d => d.ShowErrorAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>Minimal gateway capturing the open-save hand-off without touching any UI.</summary>
    private sealed class FakeGateway(bool loadResult) : ISaveFileGateway
    {
        public SaveFile? CurrentSave { get; private set; }
        public bool HasSave => CurrentSave is not null;
        public string? CurrentPath { get; private set; }
        public string? LastLoadPath { get; private set; }

        public event Action<SaveFile?>? SaveFileChanged;
        public event Action<SaveFileWrittenEventArgs>? SaveWritten { add { } remove { } }
        public event Action<SaveFile>? SaveWriteStarted { add { } remove { } }
        public event Action<SaveFile>? SaveWriteFailed { add { } remove { } }

        public bool IsReadOnly => false;
        public bool IsReadOnlySource(string path) => false;
        public Task<bool> LoadSaveFileAsync(string path, bool readOnly) => LoadSaveFileAsync(path);

        public Task<bool> LoadSaveFileAsync(string path)
        {
            LastLoadPath = path;
            if (!loadResult)
                return Task.FromResult(false);

            CurrentSave = new SAV9SV();
            CurrentPath = path;
            SaveFileChanged?.Invoke(CurrentSave);
            return Task.FromResult(true);
        }

        public void OpenLoadedSave(SaveFile sav, string? path = null)
        {
            CurrentSave = sav;
            CurrentPath = path;
            SaveFileChanged?.Invoke(sav);
        }

        public Task<bool> SaveFileAsync(string? path = null) => Task.FromResult(true);

        public void CloseSave()
        {
            CurrentSave = null;
            CurrentPath = null;
            SaveFileChanged?.Invoke(null);
        }
    }
}
