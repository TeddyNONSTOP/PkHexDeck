using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using PKHeX.Avalonia.Views;

namespace PKHeX.Avalonia.Render.Tests;

/// <summary>
/// Hardcore mode in the composed app: the save gateway and the shell share one mode, the header shows
/// the badge, and the Save workspace pages render read-only under a banner.
/// </summary>
public class HardcoreRenderTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task HardcoreShowsBadge_AndSavePagesAreReadOnly(bool dark)
    {
        var previous = global::Avalonia.Application.Current!.RequestedThemeVariant;
        var root = Path.Combine(Path.GetTempPath(), "pkhex-render-" + Guid.NewGuid().ToString("N"));
        var paths = new Mock<IAppPaths>();
        paths.SetupGet(x => x.ConfigDirectory).Returns(Path.Combine(root, "config"));
        paths.SetupGet(x => x.DataDirectory).Returns(Path.Combine(root, "data"));
        paths.SetupGet(x => x.ConfigFilePath).Returns(Path.Combine(root, "config.json"));
        paths.SetupGet(x => x.LegacyConfigFilePath).Returns(Path.Combine(root, "legacy.json"));
        var settings = new AppSettings { Hardcore = { Enabled = true } };
        using var services = (ServiceProvider)App.BuildServiceProvider(paths.Object, Mock.Of<ISettingsStore>(), settings, s =>
        {
            s.AddSingleton(Mock.Of<IDialogService>());
            s.AddSingleton(Mock.Of<IWindowService>());
        });
        var vm = services.GetRequiredService<MainWindowViewModel>();
        Window? window = null;
        try
        {
            Assert.Same(vm.Hardcore, services.GetRequiredService<ISaveWriteGuard>());
            Assert.True(vm.IsHardcore);

            global::Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tests/savefiles/gen5_white2.sav"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var save = Assert.IsAssignableFrom<SaveFile>(SaveUtil.GetSaveFile(File.ReadAllBytes(Path.Combine(directory.FullName, "Tests/savefiles/gen5_white2.sav"))));
            services.GetRequiredService<ISaveFileGateway>().OpenLoadedSave(save, "fixture.sav");
            Assert.Null(vm.Hardcore.Verify(save)); // the gateway gave the mode its baseline
            window = new MainWindow { DataContext = vm, Width = 900, Height = 600 }; window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            var badge = window.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HardcoreBadge");
            Assert.True(badge.IsEffectivelyVisible);
            var banner = window.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HardcoreSaveBanner");
            Assert.False(banner.IsEffectivelyVisible); // Pokémon workspace: the box is still usable for moves

            vm.SelectWorkspaceTabCommand.Execute(MainWindowViewModel.TrainerWorkspaceTabIndex);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.True(banner.IsEffectivelyVisible);
            var trainer = window.GetVisualDescendants().OfType<TrainerEditor>().Single();
            Assert.False(trainer.IsEffectivelyEnabled);

            using var frame = new RenderTargetBitmap(new PixelSize(900, 600)); frame.Render(window);
            if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1" && Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR") is { } path)
            { Directory.CreateDirectory(path); frame.Save(Path.Combine(path, $"hardcore-trainer-{(dark ? "dark" : "light")}.png")); }

            vm.Hardcore.SetEnabled(false);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.False(badge.IsEffectivelyVisible);
            Assert.False(banner.IsEffectivelyVisible);
            Assert.True(trainer.IsEffectivelyEnabled);
        }
        finally
        {
            services.GetRequiredService<ISaveFileGateway>().CloseSave();
            window?.Close();
            global::Avalonia.Application.Current.RequestedThemeVariant = previous;
            new AppSettings().InitializeCore();
        }
    }
}
