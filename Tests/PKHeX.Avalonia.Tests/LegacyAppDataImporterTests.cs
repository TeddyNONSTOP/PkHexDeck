using System;
using System.IO;
using System.Linq;
using PKHeX.Infrastructure.Configuration;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// PkHexDeck's first start copies PKHeX-Avalonia's settings, Bank and backups across once, without touching
/// the originals.
/// </summary>
public sealed class LegacyAppDataImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkhexdeck-import-" + Guid.NewGuid().ToString("N"));

    private string OldConfig => Path.Combine(_root, "config", "PKHeX-Avalonia");
    private string OldData => Path.Combine(_root, "data", "PKHeX-Avalonia");
    private string NewConfig => Path.Combine(_root, "config", "PkHexDeck");
    private string NewData => Path.Combine(_root, "data", "PkHexDeck");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    private void SeedOldInstall()
    {
        Directory.CreateDirectory(OldConfig);
        File.WriteAllText(Path.Combine(OldConfig, "config.json"), "{\"Hardcore\":{\"Enabled\":true}}");
        Directory.CreateDirectory(Path.Combine(OldData, "Bank", "entries"));
        File.WriteAllBytes(Path.Combine(OldData, "Bank", "entries", "a.bin"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(OldData, "Backups"));
        File.WriteAllBytes(Path.Combine(OldData, "Backups", "save.bak"), [4, 5]);
    }

    private static string[] Snapshot(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(directory, f) + ":" + Convert.ToHexString(File.ReadAllBytes(f)))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void FirstStart_CopiesConfigBankAndBackups_AndLeavesTheOriginals()
    {
        SeedOldInstall();
        var oldConfig = Snapshot(OldConfig);
        var oldData = Snapshot(OldData);

        Assert.True(LegacyAppDataImporter.ImportOnce(NewConfig, NewData, OldConfig, OldData));

        Assert.Equal(oldConfig, Snapshot(NewConfig));
        Assert.Equal(oldData, Snapshot(NewData));
        Assert.Equal(oldConfig, Snapshot(OldConfig));
        Assert.Equal(oldData, Snapshot(OldData));
        Assert.False(Directory.Exists(NewConfig + ".importing"));
        Assert.False(Directory.Exists(NewData + ".importing"));
    }

    [Fact]
    public void LaterStarts_DoNothing_EvenIfTheOldInstallChanges()
    {
        SeedOldInstall();
        Assert.True(LegacyAppDataImporter.ImportOnce(NewConfig, NewData, OldConfig, OldData));
        File.WriteAllText(Path.Combine(NewConfig, "config.json"), "{}");
        File.WriteAllText(Path.Combine(OldConfig, "config.json"), "{\"changed\":true}");

        Assert.False(LegacyAppDataImporter.ImportOnce(NewConfig, NewData, OldConfig, OldData));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(NewConfig, "config.json")));
    }

    [Fact]
    public void NoOldInstall_ImportsNothing()
    {
        Assert.False(LegacyAppDataImporter.ImportOnce(NewConfig, NewData, OldConfig, OldData));
        Assert.False(Directory.Exists(NewConfig));
        Assert.False(Directory.Exists(NewData));
    }

    [Fact]
    public void ExistingNewDataFolder_IsKept_ButConfigStillComesAcross()
    {
        SeedOldInstall();
        Directory.CreateDirectory(NewData);
        File.WriteAllText(Path.Combine(NewData, "mine.txt"), "keep");

        Assert.True(LegacyAppDataImporter.ImportOnce(NewConfig, NewData, OldConfig, OldData));

        Assert.Equal(["mine.txt:" + Convert.ToHexString("keep"u8)], Snapshot(NewData));
        Assert.True(File.Exists(Path.Combine(NewConfig, "config.json")));
    }
}
