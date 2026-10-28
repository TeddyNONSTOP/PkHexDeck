using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Emulator save discovery: the Save File Browser finds saves in emulator folders (native, Flatpak and
/// EmuDeck, including EmuDeck's symlinks and SD cards) without listing any save twice.
/// All tests run against a fake home folder; they never look at the real one.
/// </summary>
public sealed class EmulatorSaveDiscoveryTests : IDisposable
{
    private readonly string _home;

    public EmulatorSaveDiscoveryTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pkhex-emu-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch { /* best effort */ }
    }

    private static string Fixture(string name) => Path.Combine(SaveFileFixture.FindSaveFilesPath()!, name);

    private string Make(string relative)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void FindFolders_ListsOnlyExistingFoldersWithTheirEmulator()
    {
        var retroArch = Make(".var/app/org.libretro.RetroArch/config/retroarch/saves");
        var melon = Make(".config/melonDS");

        var folders = EmulatorSaveLocations.FindFolders(_home, []);

        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, f => f.Emulator == "RetroArch" && f.Path == retroArch);
        Assert.Contains(folders, f => f.Emulator == "melonDS" && f.Path == melon);
    }

    [Fact]
    public void FindFolders_EmuDeckSymlinkToTheSameFolder_IsListedOnce()
    {
        if (OperatingSystem.IsWindows())
            return;

        var retroArch = Make(".var/app/org.libretro.RetroArch/config/retroarch/saves");
        var emuDeck = Path.Combine(_home, "Emulation");
        Make("Emulation/saves/retroarch");
        Directory.CreateSymbolicLink(Path.Combine(emuDeck, "saves/retroarch/saves"), retroArch);

        var folders = EmulatorSaveLocations.FindFolders(_home, [emuDeck]);

        var single = Assert.Single(folders);
        Assert.Equal(retroArch, single.Path);
    }

    [Fact]
    public void FindFolders_EmulationFolderItselfLinkedToSdCard_ResolvesAndDedupes()
    {
        if (OperatingSystem.IsWindows())
            return;

        var sdEmulation = Make("sdcard/Emulation");
        var melon = Make("sdcard/Emulation/saves/melonds/saves");
        Directory.CreateSymbolicLink(Path.Combine(_home, "Emulation"), sdEmulation);

        var folders = EmulatorSaveLocations.FindFolders(_home, [Path.Combine(_home, "Emulation"), sdEmulation]);

        var single = Assert.Single(folders);
        Assert.Equal(melon, single.Path);
    }

    [Fact]
    public void GetEmuDeckRoots_IncludesHomeAndSdCardMounts()
    {
        var media = Make("media");
        Make("media/deck/SN01T/Emulation");
        Make("media/OLDCARD/Emulation");

        var roots = EmulatorSaveLocations.GetEmuDeckRoots(_home, media);

        Assert.Contains(Path.Combine(_home, "Emulation"), roots);
        Assert.Contains(Path.Combine(media, "deck", "SN01T", "Emulation"), roots);
        Assert.Contains(Path.Combine(media, "OLDCARD", "Emulation"), roots);
    }

    [Fact]
    public void FindCandidateFiles_KeepsSaveSizedFilesAndSkipsJunk()
    {
        var folder = Make(".config/retroarch/saves");
        File.Copy(Fixture("gen3_emerald.sav"), Path.Combine(folder, "Pokemon Emerald.srm"));
        File.WriteAllBytes(Path.Combine(folder, "notes.txt"), new byte[12]);

        var files = EmulatorSaveLocations.FindCandidateFiles(new EmulatorSaveFolder("RetroArch", folder), CancellationToken.None).ToList();

        var file = Assert.Single(files);
        Assert.Equal("RetroArch", file.Emulator);
        Assert.EndsWith("Pokemon Emerald.srm", file.Path);
    }

    [Fact]
    public void FindCandidateFiles_Ryujinx_ListsOnlyTheCommittedCopy()
    {
        var save = Make(".config/Ryujinx/bis/user/save/0000000000000001");
        Make(".config/Ryujinx/bis/user/save/0000000000000001/0");
        Make(".config/Ryujinx/bis/user/save/0000000000000001/1");
        File.Copy(Fixture("gen8b_brilliantdiamond.bin"), Path.Combine(save, "0", "SaveData.bin"));
        File.Copy(Fixture("gen8b_brilliantdiamond.bin"), Path.Combine(save, "1", "SaveData.bin"));

        var root = new EmulatorSaveFolder("Ryujinx", Path.Combine(_home, ".config/Ryujinx/bis/user/save"));
        var files = EmulatorSaveLocations.FindCandidateFiles(root, CancellationToken.None).ToList();

        var file = Assert.Single(files);
        Assert.Equal(Path.Combine(save, "0", "SaveData.bin"), file.Path);
    }

    [Fact]
    public void FindCandidateFiles_SymlinkLoop_DoesNotHang()
    {
        if (OperatingSystem.IsWindows())
            return;

        var folder = Make(".config/melonDS");
        Directory.CreateSymbolicLink(Path.Combine(folder, "loop"), folder);
        File.Copy(Fixture("gen5_black.sav"), Path.Combine(folder, "black.sav"));

        var files = EmulatorSaveLocations.FindCandidateFiles(new EmulatorSaveFolder("melonDS", folder), CancellationToken.None).ToList();

        Assert.Single(files);
    }

    [Fact]
    public async Task FolderList_ShowsEmulatorSavesWithTheirEmulatorAndFiltersByIt()
    {
        var retroArch = Make(".config/retroarch/saves");
        var melon = Make(".config/melonDS");
        File.Copy(Fixture("gen3_emerald.sav"), Path.Combine(retroArch, "emerald.srm"));
        File.Copy(Fixture("gen5_black.sav"), Path.Combine(melon, "black.sav"));
        EmulatorSaveFolder[] folders = [new("RetroArch", retroArch), new("melonDS", melon)];

        var vm = new FolderListViewModel(Mock.Of<ISaveFileGateway>(), new AppSettings(), Mock.Of<IDialogService>(),
            loadOnConstruct: false, findEmulatorFolders: () => folders);
        await vm.LoadSavesAsync(CancellationToken.None);

        Assert.Equal(2, vm.EmulatorSaves.Count);
        Assert.Contains(vm.EmulatorSaves, s => s.Emulator == "RetroArch" && s.FileName == "emerald.srm");
        Assert.Contains(vm.EmulatorSaves, s => s.Emulator == "melonDS" && s.FileName == "black.sav");

        vm.FilterText = "melonDS";
        var shown = Assert.Single(vm.FilteredEmulatorSaves);
        Assert.Equal("black.sav", shown.FileName);
    }
}
