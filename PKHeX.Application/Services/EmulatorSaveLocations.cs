using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>An emulator save folder that exists on this machine, already resolved through any symlinks.</summary>
public sealed record EmulatorSaveFolder(string Emulator, string Path);

/// <summary>A file inside an emulator save folder whose size matches a known save format.</summary>
public sealed record EmulatorSaveCandidate(string Emulator, string Path);

/// <summary>
/// Knows where emulators that run Pokémon games keep their saves on Linux: native installs, Flatpak
/// sandboxes (~/.var/app) and EmuDeck's Emulation/saves folder on internal storage or an SD card.
/// Only folders that actually exist are returned, and folders reached through several symlinks
/// (EmuDeck links most of its save folders back to the emulator's own) are listed once.
/// </summary>
public static class EmulatorSaveLocations
{
    private const int MaxScanDepth = 10;

    // (emulator, path relative to the home folder)
    private static readonly (string Emulator, string Relative)[] HomeFolders =
    [
        ("RetroArch", ".config/retroarch/saves"),
        ("RetroArch", ".var/app/org.libretro.RetroArch/config/retroarch/saves"),
        ("mGBA", ".config/mgba"),
        ("mGBA", ".var/app/io.mgba.mGBA/config/mgba"),
        ("melonDS", ".config/melonDS"),
        ("melonDS", ".var/app/net.kuribo64.melonDS/data/melonDS"),
        ("DeSmuME", ".config/desmume"),
        ("DeSmuME", ".var/app/org.desmume.DeSmuME/config/desmume"),
        ("Dolphin", ".local/share/dolphin-emu/GC"),
        ("Dolphin", ".var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu/GC"),
        ("Azahar", ".local/share/azahar-emu/sdmc"),
        ("Azahar", ".var/app/org.azahar_emu.Azahar/data/azahar-emu/sdmc"),
        ("Citra", ".local/share/citra-emu/sdmc"),
        ("Lime3DS", ".local/share/lime3ds-emu/sdmc"),
        ("Ryujinx", ".config/Ryujinx/bis/user/save"),
        ("Ryujinx", ".var/app/org.ryujinx.Ryujinx/config/Ryujinx/bis/user/save"),
        ("Yuzu", ".local/share/yuzu/nand/user/save"),
        ("Eden", ".local/share/eden/nand/user/save"),
        ("Citron", ".local/share/citron/nand/user/save"),
        ("Sudachi", ".local/share/sudachi/nand/user/save"),
    ];

    // (emulator, path relative to an EmuDeck "Emulation" folder)
    private static readonly (string Emulator, string Relative)[] EmuDeckFolders =
    [
        ("RetroArch", "saves/retroarch/saves"),
        ("mGBA", "saves/mgba/saves"),
        ("melonDS", "saves/melonds/saves"),
        ("Dolphin", "saves/dolphin/GC"),
        ("Azahar", "saves/azahar/saves"),
        ("Citra", "saves/citra/saves"),
        ("Lime3DS", "saves/lime3ds/saves"),
        ("Ryujinx", "saves/ryujinx/saves"),
        ("Yuzu", "saves/yuzu/saves"),
        ("Eden", "saves/eden/saves"),
        ("Citron", "saves/citron/saves"),
    ];

    /// <summary>Emulator save folders that exist on this machine. Empty on platforms other than Linux.</summary>
    public static IReadOnlyList<EmulatorSaveFolder> FindFolders()
    {
        if (!OperatingSystem.IsLinux())
            return [];

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return FindFolders(home, GetEmuDeckRoots(home, "/run/media"));
    }

    /// <summary>The EmuDeck "Emulation" folders: in the home folder and on any mounted drive or SD card.</summary>
    public static IReadOnlyList<string> GetEmuDeckRoots(string home, string mediaRoot)
    {
        var roots = new List<string> { Path.Combine(home, "Emulation") };
        // SteamOS mounts SD cards at /run/media/<user>/<label>, older images at /run/media/<label>.
        foreach (var mount in SafeDirectories(mediaRoot))
        {
            roots.Add(Path.Combine(mount, "Emulation"));
            roots.AddRange(SafeDirectories(mount).Select(child => Path.Combine(child, "Emulation")));
        }
        return roots;
    }

    public static IReadOnlyList<EmulatorSaveFolder> FindFolders(string home, IEnumerable<string> emuDeckRoots)
    {
        var candidates = HomeFolders.Select(f => (f.Emulator, Path: Path.Combine(home, f.Relative)))
            .Concat(emuDeckRoots.SelectMany(root => EmuDeckFolders.Select(f => (f.Emulator, Path: Path.Combine(root, f.Relative)))));

        var result = new List<EmulatorSaveFolder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (emulator, path) in candidates)
        {
            var real = GetRealPath(path);
            if (real is null || !Directory.Exists(real) || !seen.Add(real))
                continue;
            result.Add(new EmulatorSaveFolder(emulator, real));
        }
        return result;
    }

    /// <summary>
    /// Files under <paramref name="folder"/> whose size matches a known save format. Symlinked folders
    /// inside it are not followed (they could loop), and only Ryujinx's committed save copy is listed.
    /// </summary>
    public static IEnumerable<EmulatorSaveCandidate> FindCandidateFiles(EmulatorSaveFolder folder, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((folder.Path, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();

            FileInfo[] files;
            DirectoryInfo[] children;
            try
            {
                var info = new DirectoryInfo(directory);
                files = info.GetFiles();
                children = depth < MaxScanDepth ? info.GetDirectories() : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep scanning sibling folders when one is unreadable.
                continue;
            }

            foreach (var file in files)
            {
                if (file.LinkTarget is null && SaveUtil.IsSizeValid(file.Length))
                    yield return new EmulatorSaveCandidate(folder.Emulator, file.FullName);
            }

            foreach (var child in children)
            {
                if (child.LinkTarget is not null || IsRyujinxWorkingCopy(child))
                    continue;
                pending.Push((child.FullName, depth + 1));
            }
        }
    }

    // Ryujinx keeps each save as "0" (committed) and "1" (working copy, rebuilt from "0" when a game
    // starts). Only "0" is worth editing, so "1" is skipped whenever a sibling "0" exists.
    private static bool IsRyujinxWorkingCopy(DirectoryInfo directory)
        => directory.Name == "1" && directory.Parent is { } parent && Directory.Exists(Path.Combine(parent.FullName, "0"));

    /// <summary>Resolves every symlink along <paramref name="path"/>; null when part of it is missing.</summary>
    public static string? GetRealPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "/";
            var current = root;
            var parts = new Queue<string>(full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
            var hops = 0;
            while (parts.Count > 0)
            {
                var next = Path.Combine(current, parts.Dequeue());
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (!info.Exists)
                    return null;

                if (info.LinkTarget is { } target)
                {
                    if (++hops > 40)
                        return null; // symlink loop
                    var resolved = Path.GetFullPath(target, current);
                    var remaining = parts.ToArray();
                    var rootOfTarget = Path.GetPathRoot(resolved) ?? "/";
                    current = rootOfTarget;
                    parts = new Queue<string>(resolved[rootOfTarget.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Concat(remaining));
                    continue;
                }
                current = next;
            }
            return current;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetDirectories(path) : []; }
        catch { return []; }
    }
}
