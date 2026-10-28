using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Render.Tests;

public class NuzlockeReportRenderTests
{
    [AvaloniaTheory]
    [InlineData("en", false)] [InlineData("en", true)]
    [InlineData("de", false)] [InlineData("ja", true)]
    public async Task ReportFitsItsToolWindowWithSpritesHeadersAndActions(string language, bool dark)
    {
        var previousLanguage = LocalizedStrings.Instance.CurrentLanguage;
        var application = global::Avalonia.Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var save = new SAV4Pt { OT = "LUCAS", TID16 = 12345, SID16 = 54321 };
            var catches = new (Species Species, ushort Location, int Day)[]
            {
                (Species.Starly, 16, 1), (Species.Shinx, 16, 4), (Species.Staravia, 17, 2), (Species.Bidoof, 17, 3),
            };
            for (var i = 0; i < catches.Length; i++)
            {
                var (species, location, day) = catches[i];
                var pk = new PK4 { Species = (ushort)species, CurrentLevel = 5, MetLevel = 5, MetLocation = location, Version = GameVersion.Pt,
                    OriginalTrainerName = save.OT, ID32 = save.ID32, Language = (int)LanguageID.English };
                pk.MetDate = new DateOnly(2026, 1, day);
                pk.SetDefaultNickname();
                pk.SetUnshiny();
                pk.RefreshChecksum();
                save.SetBoxSlotAtIndex(pk, 0, i);
            }
            var renderer = new AvaloniaSpriteRenderer(new AppSettings());
            renderer.Initialize(save);
            var vm = new NuzlockeReportViewModel(save, renderer);
            Assert.Equal(2, vm.Rows.Count);
            Assert.NotNull(vm.Rows[0].Sprite);

            var view = new NuzlockeReportView { DataContext = vm };
            window = new Window { Content = view, Width = 700, Height = 460 };
            WindowService.ConstrainToolBounds(window);
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            AssertVisible(window, view.FindControl<TextBlock>("Summary")!);
            AssertVisible(window, view.FindControl<ComboBox>("FilterBox")!);
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.Command is not null))
                AssertVisible(window, button);
            var grid = view.FindControl<DataGrid>("NuzlockeGrid")!;
            var first = Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>(), r => r.DataContext == vm.Rows[0]);
            AssertVisible(window, first);
            Assert.Contains(first.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);
            var headers = view.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Where(h => h.IsEffectivelyVisible && h.Content is string { Length: > 0 }).ToArray();
            Assert.Equal(5, headers.Length);
            Capture(window, $"nuzlocke-{language}-{(dark ? "dark" : "light")}");
        }
        finally
        {
            window?.Close();
            LocalizedStrings.Instance.SetLanguage(previousLanguage);
            application.RequestedThemeVariant = previousTheme;
        }
    }

    private static void AssertVisible(Window window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
            point.Y + control.Bounds.Height <= window.ClientSize.Height + 1,
            $"{control.GetType().Name} at {point} + {control.Bounds.Size} outside {window.ClientSize}");
    }

    private static void Capture(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window);
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR") is { } path)
        {
            Directory.CreateDirectory(path);
            bitmap.Save(Path.Combine(path, $"{name}.png"));
        }
    }
}
