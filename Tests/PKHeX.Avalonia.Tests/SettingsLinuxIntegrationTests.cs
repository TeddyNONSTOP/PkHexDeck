using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// The Settings screen's Linux desktop-integration section: it must only be visible for supported
/// (Linux AppImage) launches, drive the integration service through the add/remove commands, and
/// surface localized status/error text rather than raw paths or exceptions.
/// </summary>
public class SettingsLinuxIntegrationTests
{
    private static Mock<ILinuxDesktopIntegrationService> SupportedService(
        bool registered = false, string? installedPath = null)
    {
        var mock = new Mock<ILinuxDesktopIntegrationService>();
        mock.SetupGet(s => s.IsSupported).Returns(true);
        mock.SetupGet(s => s.SourceAppImagePath).Returns("/home/user/Downloads/PKHeX-Avalonia-1.87.3-x86_64.AppImage");
        mock.SetupGet(s => s.IsRegistered).Returns(registered);
        mock.SetupGet(s => s.InstalledAppImagePath).Returns(installedPath);
        return mock;
    }

    private static SettingsViewModel Create(Mock<ILinuxDesktopIntegrationService>? service = null)
    {
        var settings = new AppSettings();
        return new SettingsViewModel(
            settings,
            new FakeSettingsStore(),
            new ThemeService(settings, new FakeSettingsStore()),
            new UiDensityService(settings, new FakeSettingsStore()),
            new LanguageService(),
            UpdateTestDoubles.Coordinator(),
            service?.Object);
    }

    [Fact]
    public void Section_IsHidden_WithoutIntegrationService()
    {
        Assert.False(Create().ShowLinuxIntegration);
    }

    [Fact]
    public void Section_IsHidden_WhenLaunchIsNotSupported()
    {
        var unsupported = new Mock<ILinuxDesktopIntegrationService>();
        unsupported.SetupGet(s => s.IsSupported).Returns(false);

        Assert.False(Create(unsupported).ShowLinuxIntegration);
    }

    [Fact]
    public void Section_IsVisible_AndReportsInstallState_ForSupportedLaunch()
    {
        var vm = Create(SupportedService());

        Assert.True(vm.ShowLinuxIntegration);
        Assert.False(vm.LinuxIntegrationRegistered);
        Assert.Equal(LocalizedStrings.Instance["Settings_LinuxIntegration_NotInstalled"], vm.LinuxIntegrationStatus);
    }

    [Fact]
    public void ExistingInstall_IsReflectedInInitialState()
    {
        var vm = Create(SupportedService(registered: true,
            installedPath: "/home/user/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage"));

        Assert.True(vm.LinuxIntegrationRegistered);
        Assert.Contains("/home/user/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage", vm.LinuxIntegrationStatus);
    }

    [Fact]
    public async Task AddToApplicationMenu_RegistersAndShowsInstalledPath()
    {
        var service = SupportedService();
        service
            .Setup(s => s.RegisterAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LinuxDesktopIntegrationResult(
                DesktopIntegrationOutcome.Success,
                InstalledPath: "/home/user/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage"))
            .Callback(() => service.SetupGet(s => s.IsRegistered).Returns(true));
        var vm = Create(service);

        await vm.AddToApplicationMenuCommand.ExecuteAsync(null);

        service.Verify(s => s.RegisterAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.LinuxIntegrationRegistered);
        Assert.Contains("PKHeX-Avalonia.AppImage", vm.LinuxIntegrationStatus);
    }

    [Fact]
    public async Task AddToApplicationMenu_Failure_ShowsLocalizedError()
    {
        var service = SupportedService();
        service
            .Setup(s => s.RegisterAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LinuxDesktopIntegrationResult(
                DesktopIntegrationOutcome.Failed, MessageKey: "LinuxIntegration_Error_Generic"));
        var vm = Create(service);

        await vm.AddToApplicationMenuCommand.ExecuteAsync(null);

        Assert.Equal(LocalizedStrings.Instance["LinuxIntegration_Error_Generic"], vm.LinuxIntegrationStatus);
        Assert.False(vm.LinuxIntegrationRegistered);
    }

    [Fact]
    public async Task RemoveFromApplicationMenu_UnregistersAndClearsState()
    {
        var service = SupportedService(registered: true,
            installedPath: "/home/user/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage");
        service
            .Setup(s => s.UnregisterAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LinuxDesktopIntegrationResult(DesktopIntegrationOutcome.Success))
            .Callback(() =>
            {
                service.SetupGet(s => s.IsRegistered).Returns(false);
                service.SetupGet(s => s.InstalledAppImagePath).Returns((string?)null);
            });
        var vm = Create(service);

        await vm.RemoveFromApplicationMenuCommand.ExecuteAsync(null);

        service.Verify(s => s.UnregisterAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.LinuxIntegrationRegistered);
    }

    [Fact]
    public void ShortenHomePath_RewritesPathsUnderHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal("~/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage",
            SettingsViewModel.ShortenHomePath(Path.Combine(home, ".local", "opt", "PKHeX-Avalonia", "PKHeX-Avalonia.AppImage")));
    }

    [Fact]
    public void ShortenHomePath_LeavesOtherPathsAlone()
    {
        Assert.Equal("/usr/share/applications/io.pkhex.avalonia.desktop",
            SettingsViewModel.ShortenHomePath("/usr/share/applications/io.pkhex.avalonia.desktop"));
        Assert.Equal("/", SettingsViewModel.ShortenHomePath("/"));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal("~", SettingsViewModel.ShortenHomePath(home));
    }
}

// Composed-view check: the section's XAML wiring (IsVisible gating + command bindings) realized
// against the real SettingsView, the same way SpriteStyleTests exercises the Sprites card.
public class SettingsLinuxIntegrationViewTests
{
    private static SettingsView CreateView(SettingsViewModel vm)
    {
        var view = new SettingsView { DataContext = vm };
        var window = new Window { Content = view, Width = 480, Height = 760 };
        using var lifetime = new HeadlessWindowLifetime(window);
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static SettingsViewModel CreateViewModel(Mock<ILinuxDesktopIntegrationService> service)
    {
        var settings = new AppSettings();
        return new SettingsViewModel(
            settings,
            new FakeSettingsStore(),
            new ThemeService(settings, new FakeSettingsStore()),
            new UiDensityService(settings, new FakeSettingsStore()),
            new LanguageService(),
            UpdateTestDoubles.Coordinator(),
            service.Object);
    }

    [AvaloniaFact]
    public void Section_IsRealized_ForSupportedAppImageLaunch()
    {
        var service = new Mock<ILinuxDesktopIntegrationService>();
        service.SetupGet(s => s.IsSupported).Returns(true);
        service.SetupGet(s => s.IsRegistered).Returns(false);
        service.SetupGet(s => s.InstalledAppImagePath).Returns((string?)null);
        var vm = CreateViewModel(service);

        var view = CreateView(vm);

        var header = view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == PKHeX.Presentation.Localization.LocalizedStrings.Instance["Settings_LinuxIntegration"]);
        Assert.NotNull(header);
        Assert.True(IsEffectivelyVisible(header!));

        // One state-driven button: Add while unregistered, never both at once.
        var addButton = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == vm.AddToApplicationMenuCommand);
        var removeButton = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == vm.RemoveFromApplicationMenuCommand);
        Assert.True(IsEffectivelyVisible(addButton));
        Assert.False(IsEffectivelyVisible(removeButton));
    }

    [AvaloniaFact]
    public void Section_ShowsOnlyRemove_OnceRegistered()
    {
        var service = new Mock<ILinuxDesktopIntegrationService>();
        service.SetupGet(s => s.IsSupported).Returns(true);
        service.SetupGet(s => s.IsRegistered).Returns(true);
        service.SetupGet(s => s.InstalledAppImagePath).Returns("/home/user/.local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage");
        var vm = CreateViewModel(service);

        var view = CreateView(vm);

        var addButton = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == vm.AddToApplicationMenuCommand);
        var removeButton = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == vm.RemoveFromApplicationMenuCommand);
        Assert.False(IsEffectivelyVisible(addButton));
        Assert.True(IsEffectivelyVisible(removeButton));
    }

    [AvaloniaFact]
    public void Section_IsNotRealized_WhenUnsupported()
    {
        var service = new Mock<ILinuxDesktopIntegrationService>();
        service.SetupGet(s => s.IsSupported).Returns(false);
        var vm = CreateViewModel(service);

        var view = CreateView(vm);

        // The section stays in the compiled tree with IsVisible=false, so assert it does not
        // render: no *effectively visible* header exists.
        var header = view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == PKHeX.Presentation.Localization.LocalizedStrings.Instance["Settings_LinuxIntegration"]);
        Assert.NotNull(header);
        Assert.False(IsEffectivelyVisible(header));
    }

    private static bool IsEffectivelyVisible(global::Avalonia.Visual visual) =>
        visual.IsVisible && visual.GetVisualAncestors().OfType<global::Avalonia.Visual>().All(a => a.IsVisible);
}
