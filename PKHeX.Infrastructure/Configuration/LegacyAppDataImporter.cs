using System;
using System.IO;

namespace PKHeX.Infrastructure.Configuration;

/// <summary>
/// Brings a PKHeX-Avalonia installation's settings, Bank and backups across to PkHexDeck the first time
/// PkHexDeck starts. It copies (never moves) and only reads the old folders, so the original program keeps
/// working unchanged; once PkHexDeck has its own config it never runs again.
/// </summary>
public static class LegacyAppDataImporter
{
    /// <summary>
    /// Copies <paramref name="oldConfigDirectory"/> and <paramref name="oldDataDirectory"/> into the new
    /// folders when the new config folder does not exist yet. Returns true when anything was copied.
    /// </summary>
    public static bool ImportOnce(string newConfigDirectory, string newDataDirectory, string oldConfigDirectory, string oldDataDirectory)
    {
        if (Directory.Exists(newConfigDirectory) || !Directory.Exists(oldConfigDirectory))
            return false;

        try
        {
            // Data first: if it fails part-way the config folder is still absent and the import is retried.
            if (Directory.Exists(oldDataDirectory) && !Directory.Exists(newDataDirectory))
                CopyDirectory(oldDataDirectory, newDataDirectory);
            CopyDirectory(oldConfigDirectory, newConfigDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not copy PKHeX-Avalonia data: {ex.Message}");
            return false;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        // Copy into a temporary sibling, then rename, so a half-copied folder is never mistaken for a finished one.
        var staging = destination + ".importing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(staging, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(staging, Path.GetRelativePath(source, file)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        Directory.Move(staging, destination);
    }
}
