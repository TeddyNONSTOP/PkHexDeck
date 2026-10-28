using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Safer saving: a save is written to a temporary file, verified and renamed into place, so a failed
/// write never leaves the original save half-overwritten.
/// </summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pkhex-atomic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void WriteAllBytes_NewFile_WritesData()
    {
        var path = Path.Combine(_tempDir, "new.sav");
        AtomicFileWriter.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllBytes_ExistingFile_ReplacesItAndLeavesNoTempFiles()
    {
        var path = Path.Combine(_tempDir, "game.sav");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9, 9 });

        AtomicFileWriter.WriteAllBytes(path, new byte[] { 4, 5 });

        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(path));
        Assert.Equal(new[] { "game.sav" }, Directory.GetFiles(_tempDir).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteAllBytes_Symlink_WritesThroughAndKeepsTheLink()
    {
        if (OperatingSystem.IsWindows())
            return; // creating symlinks needs elevated rights on Windows

        var realPath = Path.Combine(_tempDir, "real.sav");
        var linkPath = Path.Combine(_tempDir, "link.sav");
        File.WriteAllBytes(realPath, new byte[] { 1 });
        File.CreateSymbolicLink(linkPath, realPath);

        AtomicFileWriter.WriteAllBytes(linkPath, new byte[] { 7, 7 });

        Assert.NotNull(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(realPath));
    }

    [Fact]
    public void WriteAllBytes_FolderNotWritableButFileIs_FallsBackToInPlaceWrite()
    {
        if (OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(_tempDir, "game.sav");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
        File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        AtomicFileWriter.WriteAllBytes(path, new byte[] { 1, 2 });

        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllBytes_NothingWritable_ThrowsAndKeepsOriginal()
    {
        if (OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(_tempDir, "game.sav");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        Assert.ThrowsAny<Exception>(() => AtomicFileWriter.WriteAllBytes(path, new byte[] { 1 }));
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task SaveFileAsync_WriteFails_ReturnsFalseAndKeepsOriginalSave()
    {
        if (OperatingSystem.IsWindows())
            return;

        var sav = SaveFileFactory.CreateBlankSave(GameVersion.W2);
        var path = Path.Combine(_tempDir, "game.sav");
        var original = sav.Write().ToArray();
        await File.WriteAllBytesAsync(path, original);

        var settings = new AppSettings();
        settings.Backup.BAKEnabled = false;
        var service = new SaveFileService(new SaveBackupService(new FakeAppPaths()), settings);
        Assert.True(await service.LoadSaveFileAsync(path));
        service.CurrentSave!.Money = 555;

        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.False(await service.SaveFileAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
}
