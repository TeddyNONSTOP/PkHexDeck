namespace PKHeX.Application.Abstractions;

/// <summary>
/// The canonical, version-less AppImage file name. Shared by the desktop integration (which
/// installs the user-local copy under this name) and the self-update swap (which renames versioned
/// downloads to it), so a menu entry that points at the installed file survives every update.
/// </summary>
public static class LinuxAppImage
{
    public const string CanonicalFileName = AppIdentity.AppImageFileName;
}

/// <summary>
/// Registers the running Linux AppImage build with the desktop environment's application menu —
/// implemented in the Infrastructure layer so the platform-specific file-system/process work never
/// leaks into Presentation. Installs entirely into the user's XDG directories, so it behaves the
/// same on every distro and never needs elevated rights. The installed copy uses the canonical
/// stable file name (<c>PkHexDeck.AppImage</c>), which also gives the in-app self-updater a
/// path it can swap in place without ever breaking the menu entry.
/// </summary>
public interface ILinuxDesktopIntegrationService
{
    /// <summary>
    /// Whether the current launch can be integrated: running on Linux from an AppImage whose
    /// source file still exists. When <see langword="false"/>, the settings section that binds to
    /// this service stays hidden.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Absolute path of the running AppImage (the <c>APPIMAGE</c> variable), or <see langword="null"/>.</summary>
    string? SourceAppImagePath { get; }

    /// <summary>
    /// Absolute path of the user-local installed copy when one exists (regardless of which version
    /// installed it), or <see langword="null"/>.
    /// </summary>
    string? InstalledAppImagePath { get; }

    /// <summary>Whether the desktop entry is currently registered in the user's application menu.</summary>
    bool IsRegistered { get; }

    /// <summary>
    /// Copies the running AppImage to the user-local install location, installs the icon, writes the
    /// desktop entry, and refreshes the desktop database (best-effort).
    /// </summary>
    Task<LinuxDesktopIntegrationResult> RegisterAsync(CancellationToken ct = default);

    /// <summary>Removes the desktop entry and icon, preserving the installed AppImage. Idempotent.</summary>
    Task<LinuxDesktopIntegrationResult> UnregisterAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a <see cref="ILinuxDesktopIntegrationService"/> operation.</summary>
public enum DesktopIntegrationOutcome
{
    /// <summary>The operation completed successfully.</summary>
    Success,

    /// <summary>The operation cannot run in this environment (not Linux, or not launched as an AppImage).</summary>
    NotSupported,

    /// <summary>The operation failed; <see cref="LinuxDesktopIntegrationResult.MessageKey"/> carries a localization key.</summary>
    Failed,
}

/// <summary>
/// The result of registering or unregistering the desktop integration.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="InstalledPath">On a successful registration, the installed AppImage path.</param>
/// <param name="MessageKey">
/// A localization key describing a <see cref="DesktopIntegrationOutcome.Failed"/> or
/// <see cref="DesktopIntegrationOutcome.NotSupported"/> outcome, or an informational success key;
/// never a raw exception message.
/// </param>
public sealed record LinuxDesktopIntegrationResult(
    DesktopIntegrationOutcome Outcome,
    string? InstalledPath = null,
    string? MessageKey = null);
