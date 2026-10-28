using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.ViewModels;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class UtilityWindowInitialLayoutTests
{
    [AvaloniaFact]
    public async Task AboutHeightAdjustsToUpdateStatusAndKeepsBothActionsVisible()
    {
        using var app = new HeadlessAppFixture();
        var vm = new AboutViewModel(UpdateTestDoubles.Coordinator());
        var view = new AboutView { DataContext = vm };
        var window = new Window { Content = view, MaxWidth = 900, MaxHeight = 800 };
        WindowService.SetMeasuredInitialBounds(window);
        WindowService.ConfigureAboutAutoSize(window);
        window.Show();
        try
        {
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            var initialHeight = window.ClientSize.Height;
            foreach (var text in new[] { "Checking for updates…", "You're up to date (v1.87.1)", string.Join(" ", Enumerable.Repeat("Update service unavailable.", 12)), "" })
            {
                vm.UpdateCheckStatus = text;
                await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
                // The headless font metrics can fit a short status within the view's minimum
                // height. Long status must grow; actual short-status growth is also Skia-tested.
                Assert.True(window.ClientSize.Height >= initialHeight);
                if (text.Length > 100) Assert.True(window.ClientSize.Height > initialHeight);
                foreach (var button in view.GetVisualDescendants().OfType<Button>())
                {
                    var end = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value;
                    Assert.InRange(end.Y, 1, window.ClientSize.Height);
                }
            }
            Assert.Equal(initialHeight, window.ClientSize.Height);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false, 480, 240)] [InlineData(true, 620, 440)]
    public void CompactUtilitySizeIsSetBeforeShowingEvenWhenWindowStartedLarge(bool legality, int width, int height)
    {
        using var app = new HeadlessAppFixture();
        var window = new Window { Width = 1200, Height = 720, WindowState = WindowState.Maximized };
        WindowService.ConfigureCompactUtilityBounds(window, legality, 1200, 800);
        Assert.Equal(width, window.Width); Assert.Equal(height, window.Height);
        Assert.Equal(WindowState.Normal, window.WindowState); Assert.Equal(SizeToContent.Manual, window.SizeToContent);
    }
    [AvaloniaFact]
    public void SmallMeasuredContentDoesNotAdoptTheOwnersLargeInitialBounds()
    {
        using var app = new HeadlessAppFixture();
        var window = new Window { Content = new Border { Width = 400, Height = 344 }, Width = 1200, Height = 720, MaxWidth = 1200, MaxHeight = 800 };
        WindowService.SetMeasuredInitialBounds(window);
        Assert.Equal(420, window.Width); Assert.Equal(344, window.Height);
    }
    [AvaloniaFact]
    public void DatabaseActionsAreReachableOnTheFirstShownFrameWithoutResizing()
    {
        using var app = new HeadlessAppFixture();
        var vm = new PKMDatabaseViewModel(new SAV6XY(), Mock.Of<ISpriteRenderer>(), Mock.Of<IDialogService>());
        var view = new PKMDatabaseView { DataContext = vm };
        var window = new Window { Content = view, MaxWidth = 1200, MaxHeight = 650 };
        WindowService.SetMeasuredInitialBounds(window); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            foreach (var name in new[] { "DatabaseSearchSave", "DatabaseScanFolder" })
            {
                var button = view.FindControl<Button>(name)!; var point = button.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= view.Bounds.Height, $"{name} at {point.Y} in {view.Bounds.Height}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void UtilityContentFillsResizedHostInsteadOfRemainingASeparateFixedPanel(bool about)
    {
        using var app = new HeadlessAppFixture();
        Control view = about ? new AboutView { DataContext = new AboutViewModel() } : new LegalityView { DataContext = new LegalityViewModel("Legal!") };
        var window = new Window { Content = view, Width = 800, Height = 550 }; window.Show();
        try { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.Equal(window.ClientSize.Width, view.Bounds.Width); Assert.Equal(window.ClientSize.Height, view.Bounds.Height); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void UpdateActionsRemainReachableWhenErrorTextIsLong()
    {
        using var app = new HeadlessAppFixture();
        var vm = new UpdateDownloadViewModel(new ReleaseInfo("v1.79.0", "release", "notes", "https://example.com", false, []),
            new ReleaseAsset("asset.zip", "https://example.com/asset.zip"), Mock.Of<IUpdateInstaller>(), Mock.Of<IAppLifetime>());
        vm.ErrorMessage = string.Join(" ", Enumerable.Repeat("Long error detail.", 100));
        var view = new UpdateDownloadView { DataContext = vm }; var window = new Window { Content = view, Width = 480, Height = 240 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var button = view.FindControl<Button>("UpdateInstallButton")!; var point = button.TranslatePoint(default, view)!.Value;
            Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= view.Bounds.Height);
        }
        finally { window.Close(); }
    }
}
