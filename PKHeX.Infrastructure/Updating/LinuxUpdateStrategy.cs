using System.Diagnostics;
using System.IO.Compression;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;

namespace PKHeX.Infrastructure.Updating;

/// <summary>
/// Linux install strategy. AppImage: the new AppImage file is already a complete, self-contained
/// executable, so installing it is just chmod +x and an atomic move into place via a detached shell
/// helper that waits for our process to exit — landing on the canonical stable file name
/// (<see cref="LinuxAppImage.CanonicalFileName"/>) in the same directory, so the version no longer
/// sticks to whatever file name the user first downloaded. Portable (extracted-zip) install: same
/// staging + swap dance as the Windows portable path, using a POSIX shell helper instead of cmd.
/// </summary>
internal sealed class LinuxUpdateStrategy : IPlatformUpdateStrategy
{
    public Task<UpdateInstallResult> InstallAsync(
        string downloadedFilePath, ReleaseAsset asset, InstallLocationInfo location,
        IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        return location.Kind == InstallKind.LinuxAppImage
            ? SwapAppImageAsync(downloadedFilePath, location, progress)
            : SwapPortableAsync(downloadedFilePath, location, progress, ct);
    }

    private static Task<UpdateInstallResult> SwapAppImageAsync(
        string newAppImagePath, InstallLocationInfo location, IProgress<UpdateProgress> progress)
    {
        progress.Report(new UpdateProgress(UpdatePhase.Swapping, 0, null));

        MakeExecutable(newAppImagePath);

        var lines = BuildAppImageSwapScript(newAppImagePath, location.Root,
            Path.Combine(Path.GetTempPath(), "pkhex-update-helper.log"));

        var scriptPath = WriteScript(lines);
        StartDetached("/bin/sh", $"\"{scriptPath}\" {Environment.ProcessId}");

        progress.Report(new UpdateProgress(UpdatePhase.Relaunching, 0, null));
        return Task.FromResult(new UpdateInstallResult(true, true, (string?)null));
    }

    /// <summary>
    /// Stages the complete executable beside its destination before replacing anything. Renaming a
    /// versioned image uses an exclusive hard link, so an unrelated stable-name file is never replaced.
    /// A failed staging/replacement leaves the original runnable; canonical updates use atomic rename.
    /// </summary>
    internal static string[] BuildAppImageSwapScript(
        string newAppImagePath, string currentAppImagePath, string logPath)
    {
        // These are Linux paths even when the script is inspected in Windows tests.
        var separator = currentAppImagePath.LastIndexOf('/');
        var directory = separator < 0 ? "." : separator == 0 ? "/" : currentAppImagePath[..separator];
        var targetPath = separator < 0 ? LinuxAppImage.CanonicalFileName
            : currentAppImagePath[..(separator + 1)] + LinuxAppImage.CanonicalFileName;
        return $$"""
            #!/bin/sh
            PID="$1"
            LOG={{QuoteShell(logPath)}}
            CURRENT={{QuoteShell(currentAppImagePath)}}
            TARGET={{QuoteShell(targetPath)}}
            NEW={{QuoteShell(newAppImagePath)}}
            DIRECTORY={{QuoteShell(directory)}}
            WORK=''
            cleanup() { if [ -n "$WORK" ]; then rm -f -- "$WORK/image"; rmdir -- "$WORK"; fi; }
            fail() { echo "$(date) update failed; keeping original" >> "$LOG"; "$CURRENT" >> "$LOG" 2>&1 & exit 1; }
            trap cleanup EXIT
            trap 'exit 1' HUP INT TERM
            while kill -0 "$PID" 2>/dev/null; do sleep 0.5; done
            [ -f "$CURRENT" ] && [ ! -L "$CURRENT" ] || exit 1
            if [ "$CURRENT" != "$TARGET" ] && { [ -e "$TARGET" ] || [ -L "$TARGET" ]; }; then fail; fi
            WORK=$(mktemp -d "$DIRECTORY/.pkhex-update.XXXXXX") || fail
            cp -- "$NEW" "$WORK/image" 2>>"$LOG" || fail
            chmod 755 "$WORK/image" 2>>"$LOG" || fail
            if [ "$CURRENT" = "$TARGET" ]; then
                # -T refuses a directory destination; source and destination share a filesystem.
                mv -fT -- "$WORK/image" "$TARGET" 2>>"$LOG" || fail
            else
                # ln has no overwrite mode: also refuses a target created after the preflight.
                ln -T -- "$WORK/image" "$TARGET" 2>>"$LOG" || fail
                rm -f -- "$CURRENT" 2>>"$LOG"
            fi
            echo "$(date) update applied, relaunching" >> "$LOG"
            "$TARGET" >> "$LOG" 2>&1 &
            """.ReplaceLineEndings("\n").Split('\n');
    }

    private static string QuoteShell(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static async Task<UpdateInstallResult> SwapPortableAsync(
        string zipPath, InstallLocationInfo location, IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        var currentDir = location.Root.TrimEnd(Path.DirectorySeparatorChar);
        var stagingDir = Path.Combine(Path.GetTempPath(), $"pkhex-update-stage-{Guid.NewGuid():N}");

        progress.Report(new UpdateProgress(UpdatePhase.Extracting, 0, null));
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingDir), ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return new UpdateInstallResult(false, false, "Update_Error_SwapFailed");
        }

        progress.Report(new UpdateProgress(UpdatePhase.Swapping, 0, null));

        var backupDir = currentDir + ".bak";
        var logPath = Path.Combine(Path.GetTempPath(), "pkhex-update-helper.log");
        var pid = Environment.ProcessId;
        var exePath = Path.Combine(currentDir, "PKHeX.Avalonia");

        var lines = new[]
        {
            "#!/bin/sh",
            "PID=\"$1\"",
            $"LOG=\"{logPath}\"",
            "echo \"$(date) waiting for pid $PID to exit\" >> \"$LOG\"",
            "while kill -0 \"$PID\" 2>/dev/null; do sleep 0.5; done",
            "echo \"$(date) swapping install directory\" >> \"$LOG\"",
            $"rm -rf \"{backupDir}\"",
            $"cp -a \"{currentDir}\" \"{backupDir}\"",
            $"if cp -a \"{stagingDir}\"/. \"{currentDir}\"/ 2>>\"$LOG\"; then",
            $"  rm -rf \"{backupDir}\"",
            "  echo \"$(date) update applied, relaunching\" >> \"$LOG\"",
            "else",
            "  echo \"$(date) swap failed, restoring backup\" >> \"$LOG\"",
            $"  rm -rf \"{currentDir}\"",
            $"  mv \"{backupDir}\" \"{currentDir}\"",
            "fi",
            $"chmod +x \"{exePath}\" 2>/dev/null",
            $"\"{exePath}\" &",
        };

        var scriptPath = WriteScript(lines);
        StartDetached("/bin/sh", $"\"{scriptPath}\" {pid}");

        progress.Report(new UpdateProgress(UpdatePhase.Relaunching, 0, null));
        return new UpdateInstallResult(true, true, null);
    }

    private static string WriteScript(IReadOnlyList<string> lines)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pkhex-update-helper-{Guid.NewGuid():N}.sh");
        File.WriteAllText(scriptPath, string.Join('\n', lines) + "\n");
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    private static void MakeExecutable(string path)
    {
        var psi = new ProcessStartInfo("chmod") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("+x");
        psi.ArgumentList.Add(path);
        using var chmod = Process.Start(psi);
        chmod?.WaitForExit();
    }

    private static void StartDetached(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process.Start(psi);
    }
}
