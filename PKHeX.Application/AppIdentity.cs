namespace PKHeX.Application;

/// <summary>
/// Who this program is: PkHexDeck, a fork of PKHeX-Avalonia (itself a port of PKHeX). Names, the desktop
/// app ID and folder names live here so they change in one place. Internal project names (PKHeX.Core and
/// friends) are left as they are, which keeps upstream Core syncs straightforward.
/// </summary>
public static class AppIdentity
{
    /// <summary>The program's name, as shown to users and used for its config and data folders.</summary>
    public const string Name = "PkHexDeck";

    /// <summary>Reverse-DNS application ID used by the desktop entry and AppStream metadata.</summary>
    public const string AppId = "io.pkhexdeck.app";

    /// <summary>The stable file name the AppImage is installed under by "Add to applications menu".</summary>
    public const string AppImageFileName = "PkHexDeck.AppImage";

    /// <summary>The upstream program's folder name; settings, Bank and backups are copied from it once.</summary>
    public const string LegacyFolderName = "PKHeX-Avalonia";

    /// <summary>
    /// GitHub "owner/repository" that publishes PkHexDeck releases, or <see langword="null"/> while there is
    /// none. Without one, no update check or self-update is made and their controls are hidden.
    /// </summary>
    public static readonly string? ReleasesRepository = null;

    /// <summary>True when there is a release repository to check for updates.</summary>
    public static bool HasUpdates => ReleasesRepository is not null;
}
