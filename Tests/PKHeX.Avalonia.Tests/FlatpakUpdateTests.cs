using Moq;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class FlatpakUpdateTests
{
    [Fact]
    public async Task Package_managed_update_keeps_notes_but_disables_download_and_browser_fallback()
    {
        var installer = new Mock<IUpdateInstaller>(MockBehavior.Strict);
        installer.SetupGet(i => i.CurrentInstallKind).Returns(InstallKind.LinuxFlatpak);
        var windows = new Mock<IWindowService>(MockBehavior.Strict);
        var lifetime = new Mock<IAppLifetime>(MockBehavior.Strict);
        ReleaseInfo[] releases = [new("v99.0.0", "New release", "Notes", "https://example.com/notes", false,
            [new ReleaseAsset("PKHeX-Avalonia-linux-x64.zip", "https://example.com/portable.zip")])];
        var changelog = new UpdateChangelogViewModel(releases, installer.Object, windows.Object, lifetime.Object);
        var notification = new UpdateNotificationViewModel(releases, windows.Object, installer.Object,
            lifetime.Object, new AppSettings(), new FakeSettingsStore());
        Assert.True(changelog.IsFlatpak);
        Assert.Single(changelog.Releases);
        Assert.False(changelog.DownloadCommand.CanExecute(null));
        Assert.False(notification.DownloadCommand.CanExecute(null));
        // ExecuteAsync can be called directly without checking CanExecute; the shared launcher
        // must still refuse both the installer and browser fallback.
        await changelog.DownloadCommand.ExecuteAsync(null);
        await notification.DownloadCommand.ExecuteAsync(null);
        installer.VerifyGet(i => i.CurrentInstallKind, Times.AtLeastOnce);
        installer.VerifyNoOtherCalls();
        windows.VerifyNoOtherCalls();
        lifetime.VerifyNoOtherCalls();
    }
}
