using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests.Harness;

/// <summary>
/// Composed-app rendering of the Bank window content (real DI graph, real sprite renderer, real
/// wallpaper catalog). Always asserts the realized layout; with <c>PKHEX_HEADLESS_CAPTURE=1</c> it also
/// writes PNG frames to <c>PKHEX_HEADLESS_CAPTURE_DIR</c> for visual review.
/// </summary>
public sealed class HeadlessBankViewTests(ITestOutputHelper output)
{
    private static PK9 Mon(SaveFile sav, ushort species, string nickname, bool shiny = false)
    {
        var pk = (PK9)sav.BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        pk.IsNicknamed = true;
        pk.CurrentLevel = 50;
        if (shiny)
            pk.SetShiny();
        pk.RefreshChecksum();
        return pk;
    }

    private static Guid Deposit(IBankService bank, PKM pk) =>
        bank.Add(BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "Scarlet (Ann)")).Entry!.Id;

    private void Capture(HeadlessAppFixture app, string name)
    {
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") != "1")
        {
            output.WriteLine("Capture skipped: set PKHEX_HEADLESS_CAPTURE=1 with the Skia headless app builder.");
            return;
        }

        var directory = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "pkhex-bank-captures");
        var saved = app.CaptureFrame(Path.Combine(directory, name));
        Assert.NotNull(saved);
        output.WriteLine($"Wrote {saved}");
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task BankView_RendersTheBoxGrid_WithAPendingWithdrawalAndWallpaper(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture();
        try
        {
            app.ViewModel.LanguageService.SetLanguage(language);
            var save = BlankSaveFile.Get(GameVersion.SL);
            app.LoadSaveInstance(save);
            var bank = app.Services.GetRequiredService<IBankService>();

            var pending = Deposit(bank, Mon(save, 25, "Sparky"));
            Deposit(bank, Mon(save, 6, "Blaze", shiny: true));
            Deposit(bank, Mon(save, 133, "Eevee"));
            Deposit(bank, Mon(save, 906, "Sprig"));
            Deposit(bank, Mon(save, 149, "Dragonite"));
            bank.SetBoxName(0, "Favourites");
            bank.SetBoxWallpaper(0, BankWallpaper.Art("swsh:box_wp05swsh"));
            await app.ViewModel.WithdrawAsync(pending);
            Assert.Empty(app.Dialogs.Errors);

            var vm = app.ViewModel.Bank!;
            vm.RefreshCurrentBox();
            vm.SelectSlotByClickCommand.Execute(vm.Slots[1]);

            var view = new BankView { DataContext = vm };
            app.Window.Width = 780;
            app.Window.Height = 660;
            app.Window.Content = view;
            app.Pump();
            app.PumpUntil(() => view.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("slot")) == 30,
                because: "all 30 Bank slots to realize");

            var slots = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("slot")).ToList();
            Assert.All(slots, s => Assert.True(s.Bounds.Width > 40 && s.Bounds.Height > 40, $"slot too small: {s.Bounds}"));
            Assert.Contains("pending", slots[0].Classes);
            Assert.Contains("selected", slots[1].Classes);
            Assert.False(view.FindControl<Border>("ReadOnlyBanner")!.IsVisible);

            var withdraw = view.FindControl<Button>("WithdrawButton")!;
            Assert.True(withdraw.IsEffectivelyEnabled);
            Assert.True(withdraw.Bounds.Width > 0);
            Assert.Equal("Favourites", view.FindControl<TextBlock>("BoxNameText")!.Text);
            Assert.Contains(GameInfo.GetVersionName(GameVersion.SL), view.FindControl<TextBlock>("TargetText")!.Text);

            // Everything stays inside the window at this size (no clipped footer actions).
            var restore = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.RestoreSelectedBackupCommand);
            var bottom = restore.TranslatePoint(new Point(0, restore.Bounds.Height), view)!.Value.Y;
            Assert.True(bottom <= view.Bounds.Height + 0.5, $"footer clipped: {bottom} > {view.Bounds.Height}");

            Capture(app, $"bank-{language}.png");
        }
        finally
        {
            app.ViewModel.LanguageService.SetLanguage(previous);
        }
    }

    [AvaloniaFact]
    public void BankView_ReadOnlyBank_ShowsTheBanner()
    {
        var paths = new FakeAppPaths();
        var root = Path.Combine(paths.DataDirectory, "bank");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.json"), "{corrupt");
        using var app = new HeadlessAppFixture(svc => svc.AddSingleton<IBankService>(new PKHeX.Infrastructure.Bank.FileBankService(paths)));

        var view = new BankView { DataContext = app.ViewModel.Bank };
        app.Window.Width = 780;
        app.Window.Height = 660;
        app.Window.Content = view;
        app.Pump();

        Assert.True(view.FindControl<Border>("ReadOnlyBanner")!.IsVisible);
        Assert.False(view.FindControl<TextBox>("BoxNameBox")!.IsEnabled);
        Assert.False(view.FindControl<Button>("WithdrawButton")!.IsEffectivelyEnabled);
        Capture(app, "bank-readonly.png");
    }

    [AvaloniaFact]
    public void WallpaperPicker_RendersColorsAndBundledArt()
    {
        using var app = new HeadlessAppFixture();
        var picker = new WallpaperPickerViewModel(app.Services.GetRequiredService<IBoxWallpaperCatalog>(), BankWallpaper.Color(3));
        var view = new WallpaperPickerView { DataContext = picker };
        app.Window.Width = 600;
        app.Window.Height = 560;
        app.Window.Content = view;
        app.Pump();

        var choices = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("wallpaper-choice")).ToList();
        Assert.True(choices.Count >= BankWallpaperPalette.Colors.Count + 20, $"only {choices.Count} choices realized");
        Assert.Contains(choices, c => c.Classes.Contains("selected"));
        Capture(app, "bank-wallpaper-picker.png");
    }
}
