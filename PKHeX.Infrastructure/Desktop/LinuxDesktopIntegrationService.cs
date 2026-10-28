using System.Diagnostics;
using System.Text;
using PKHeX.Application.Abstractions;

namespace PKHeX.Infrastructure.Desktop;

/// <summary>
/// User-local (XDG) desktop integration for the Linux AppImage build. Registration copies the
/// running AppImage to <c>~/.local/opt/PkHexDeck/PkHexDeck.AppImage</c> under the
/// canonical stable name, installs the bundled icon into the user's hicolor theme, writes an
/// <c>io.pkhexdeck.app.desktop</c> entry into the user's applications directory, and refreshes the
/// desktop database best-effort. Everything lands in per-user directories without elevated rights.
/// On supported Linux desktops, because the installed file
/// keeps the stable name, the in-app self-updater swaps it in place without breaking the entry.
/// </summary>
public sealed class LinuxDesktopIntegrationService : ILinuxDesktopIntegrationService
{
    private const string DesktopEntryId = PKHeX.Application.AppIdentity.AppId;

    // Layout mirrors the AppImage's own AppDir (prepare-appdir.sh): the 64px icon is the one the
    // bundle ships at usr/share/icons/hicolor/64x64/apps.
    private static readonly string IconRelativePath = Path.Combine(
        "icons", "hicolor", "64x64", "apps", $"{DesktopEntryId}.png");

    private readonly string? _sourceAppImagePath;
    private readonly string? _appDirPath;
    private readonly string _dataHome;
    private readonly string _optInstallDir;
    private readonly bool _isLinux;
    private readonly bool _isFlatpak;
    private readonly Func<string, IReadOnlyList<string>, int>? _runCommand;

    /// <summary>Reads everything impure from the environment; the internal ctor passes it all in for tests.</summary>
    public LinuxDesktopIntegrationService()
        : this(
            Environment.GetEnvironmentVariable("APPIMAGE"),
            Environment.GetEnvironmentVariable("APPDIR"),
            ResolveDataHome(),
            Path.Combine(ResolveHome(), ".local", "opt", PKHeX.Application.AppIdentity.Name),
            OperatingSystem.IsLinux(),
            runCommand: null,
            isFlatpak: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")))
    {
    }

    internal LinuxDesktopIntegrationService(
        string? sourceAppImagePath,
        string? appDirPath,
        string dataHome,
        string optInstallDir,
        bool isLinux,
        Func<string, IReadOnlyList<string>, int>? runCommand,
        bool isFlatpak = false)
    {
        _sourceAppImagePath = sourceAppImagePath;
        _appDirPath = appDirPath;
        _dataHome = dataHome;
        _optInstallDir = optInstallDir;
        _isLinux = isLinux;
        _isFlatpak = isFlatpak;
        _runCommand = runCommand;
    }

    private string ApplicationsDir => Path.Combine(_dataHome, "applications");
    private string DesktopEntryPath => Path.Combine(ApplicationsDir, $"{DesktopEntryId}.desktop");
    private string InstalledIconPath => Path.Combine(_dataHome, IconRelativePath);

    public bool IsSupported => _isLinux && !_isFlatpak
        && !string.IsNullOrEmpty(_sourceAppImagePath)
        && File.Exists(_sourceAppImagePath);

    public string? SourceAppImagePath => IsSupported ? _sourceAppImagePath : null;

    public string? InstalledAppImagePath
    {
        get
        {
            var path = InstalledAppImagePathUnchecked;
            return File.Exists(path) ? path : null;
        }
    }

    private string InstalledAppImagePathUnchecked => Path.Combine(_optInstallDir, LinuxAppImage.CanonicalFileName);

    public bool IsRegistered => File.Exists(DesktopEntryPath);

    public Task<LinuxDesktopIntegrationResult> RegisterAsync(CancellationToken ct = default) =>
        Task.Run(() => Register(ct), ct);

    public Task<LinuxDesktopIntegrationResult> UnregisterAsync(CancellationToken ct = default) =>
        Task.Run(() => Unregister(ct), ct);

    private LinuxDesktopIntegrationResult Register(CancellationToken ct)
    {
        if (!IsSupported)
            return NotSupported();

        try
        {
            ct.ThrowIfCancellationRequested();

            // Validate the launch path before changing any installed files.
            _ = LinuxDesktopEntry.QuoteExec(InstalledAppImagePathUnchecked);
            Directory.CreateDirectory(_optInstallDir);
            CopyAtomic(_sourceAppImagePath!, InstalledAppImagePathUnchecked, ct, executable: true);

            // The icon is copied out of the mounted AppDir when present; without it the entry still
            // works, it just shows a generic icon until the user installs a theme icon themselves.
            var appDirIcon = _appDirPath is null ? null : Path.Combine(_appDirPath,
                "usr", "share", "icons", "hicolor", "64x64", "apps", $"{DesktopEntryId}.png");
            var iconInstalled = false;
            if (appDirIcon is not null && File.Exists(appDirIcon))
            {
                var iconDir = Path.GetDirectoryName(InstalledIconPath)!;
                Directory.CreateDirectory(iconDir);
                CopyAtomic(appDirIcon, InstalledIconPath, ct);
                iconInstalled = true;
            }

            WriteDesktopEntry(InstalledAppImagePathUnchecked, iconInstalled);

            RefreshDesktopDatabases();
            return new LinuxDesktopIntegrationResult(
                DesktopIntegrationOutcome.Success, InstalledAppImagePathUnchecked);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Linux desktop integration failed: {ex.Message}");
            return new LinuxDesktopIntegrationResult(
                DesktopIntegrationOutcome.Failed, null, "LinuxIntegration_Error_Generic");
        }
    }

    private LinuxDesktopIntegrationResult Unregister(CancellationToken ct)
    {
        if (!_isLinux || _isFlatpak)
            return NotSupported();

        try
        {
            ct.ThrowIfCancellationRequested();

            // This is menu removal, not an uninstall. Keep the running/installed image so
            // the user can add it again. Required deletions must surface failures.
            DeleteRegistrationFile(DesktopEntryPath);
            DeleteRegistrationFile(InstalledIconPath);

            RefreshDesktopDatabases();
            return new LinuxDesktopIntegrationResult(DesktopIntegrationOutcome.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Linux desktop integration removal failed: {ex.Message}");
            return new LinuxDesktopIntegrationResult(
                DesktopIntegrationOutcome.Failed, null, "LinuxIntegration_Error_Generic");
        }
    }

    private LinuxDesktopIntegrationResult NotSupported() => new(
        DesktopIntegrationOutcome.NotSupported, null, "LinuxIntegration_Error_NotAppImage");

    private void WriteDesktopEntry(string execPath, bool iconInstalled)
    {
        var entry = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine($"Name={PKHeX.Application.AppIdentity.Name}")
            .AppendLine("Comment=Pokémon save file editor")
            // GLib resolves the executable before field-code expansion. Keep literal % in an
            // argument to env instead, so %% is expanded before the AppImage path is resolved.
            .AppendLine($"Exec=/usr/bin/env -- {LinuxDesktopEntry.QuoteExec(execPath)}")
            .AppendLine($"TryExec={LinuxDesktopEntry.EscapeValue(execPath)}")
            .AppendLine("Categories=Utility;")
            .AppendLine("Terminal=false")
            .AppendLine("StartupWMClass=PKHeX.Avalonia");
        if (iconInstalled)
            entry.AppendLine($"Icon={DesktopEntryId}");

        Directory.CreateDirectory(ApplicationsDir);
        WriteAtomic(DesktopEntryPath, entry.ToString());
    }

    private static void DeleteRegistrationFile(string path)
    {
        // File.Delete is idempotent for a missing file, but its parent may not exist yet.
        if (Directory.Exists(Path.GetDirectoryName(path)))
            File.Delete(path);
    }

    private static void CopyAtomic(string sourcePath, string destinationPath, CancellationToken ct, bool executable = false)
    {
        if (Path.GetFullPath(sourcePath) == Path.GetFullPath(destinationPath))
            return;
        var stagingPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var source = File.OpenRead(sourcePath))
            using (var destination = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    destination.Write(buffer, 0, read);
                }
            }
            ct.ThrowIfCancellationRequested();
            if (executable)
                MakeExecutable(stagingPath);
            File.Move(stagingPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    private static void WriteAtomic(string path, string contents)
    {
        var stagingPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(stagingPath, contents, new UTF8Encoding(false));
            File.Move(stagingPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsLinux())
            return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private void RefreshDesktopDatabases()
    {
        // Purely best-effort: menu scanners pick up user entries on their own; these just make it
        // immediate on distros that ship the helpers.
        RunBestEffort("update-desktop-database", [ApplicationsDir]);
        var hicolorRoot = Path.Combine(_dataHome, "icons", "hicolor");
        RunBestEffort("gtk-update-icon-cache", ["-f", "-t", hicolorRoot]);
    }

    private void RunBestEffort(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            if (_runCommand is { } run)
            {
                run(fileName, arguments);
                return;
            }

            var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Missing helper or blocked exec — the entry still works, refresh happens naturally.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string ResolveHome() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ResolveDataHome()
    {
        var local = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrEmpty(local) && Path.IsPathFullyQualified(local)
            ? local : Path.Combine(ResolveHome(), ".local", "share");
    }
}
