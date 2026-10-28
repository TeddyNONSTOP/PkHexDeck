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

public class LivingDexTrackerRenderTests
{
    [AvaloniaTheory]
    [InlineData("en", false)] [InlineData("en", true)]
    [InlineData("de", false)] [InlineData("ja", true)]
    public async Task TrackerFitsItsToolWindowWithSpritesHeadersAndActions(string language, bool dark)
    {
        var previousLanguage = LocalizedStrings.Instance.CurrentLanguage;
        var application = global::Avalonia.Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var save = BlankSaveFile.Get(GameVersion.SL);
            foreach (var (species, slot, shiny) in new[] { ((ushort)1, 0, false), ((ushort)4, 1, true), ((ushort)25, 2, false), ((ushort)25, 3, true) })
            {
                var pk = save.BlankPKM;
                pk.Species = species;
                pk.CurrentLevel = 5;
                if (shiny) pk.SetShiny(); else pk.SetUnshiny();
                pk.RefreshChecksum();
                save.SetBoxSlotAtIndex(pk, 0, slot);
            }
            var renderer = new AvaloniaSpriteRenderer(new AppSettings());
            renderer.Initialize(save);
            var vm = new LivingDexTrackerViewModel(save, renderer);
            Assert.NotNull(vm.Rows[0].Sprite);

            var view = new LivingDexTrackerView { DataContext = vm };
            window = new Window { Content = view, Width = 700, Height = 460 };
            WindowService.ConstrainToolBounds(window);
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            AssertVisible(window, view.FindControl<TextBlock>("Summary")!);
            AssertVisible(window, view.FindControl<ComboBox>("FilterBox")!);
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.Command is not null))
                AssertVisible(window, button);
            var headers = view.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Where(h => h.IsEffectivelyVisible && h.Content is string { Length: > 0 }).ToArray();
            Assert.Equal(5, headers.Length);
            foreach (var header in headers) AssertVisible(window, header);

            var grid = view.FindControl<DataGrid>("TrackerGrid")!;
            var first = Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>(), r => r.DataContext == vm.Rows[0]);
            AssertVisible(window, first);
            Assert.Contains(first.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);
            Capture(window, $"livingdex-{language}-{(dark ? "dark" : "light")}");

            vm.FilterIndex = (int)LivingDexTrackerFilter.Owned;
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.Equal(3, vm.Rows.Count);
            Capture(window, $"livingdex-{language}-{(dark ? "dark" : "light")}-owned");
        }
        finally
        {
            window?.Close();
            LocalizedStrings.Instance.SetLanguage(previousLanguage);
            application.RequestedThemeVariant = previousTheme;
        }
    }

    [AvaloniaTheory]
    [InlineData("en", false)] [InlineData("en", true)] [InlineData("de", false)]
    public async Task ArrangeWindowShowsPreviewOptionsAndActions(string language, bool dark)
    {
        var previousLanguage = LocalizedStrings.Instance.CurrentLanguage;
        var application = global::Avalonia.Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var save = BlankSaveFile.Get(GameVersion.SL);
            foreach (var (species, slot) in new[] { ((ushort)25, 0), ((ushort)4, 3), ((ushort)1, 7), ((ushort)25, 9) })
            {
                var pk = save.BlankPKM;
                pk.Species = species;
                pk.CurrentLevel = 5;
                pk.SetUnshiny();
                pk.RefreshChecksum();
                save.SetBoxSlotAtIndex(pk, 0, slot);
            }
            var undo = new UndoRedoService();
            undo.Initialize(save);
            var vm = new LivingDexArrangeViewModel(save, undo, new Moq.Mock<PKHeX.Application.Abstractions.IDialogService>().Object);
            Assert.NotEmpty(vm.Moves);

            var view = new LivingDexArrangeView { DataContext = vm };
            window = new Window { Content = view, Width = 640, Height = 440 };
            WindowService.ConstrainToolBounds(window);
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            AssertVisible(window, view.FindControl<TextBlock>("ArrangeSummary")!);
            AssertVisible(window, view.FindControl<ComboBox>("StartBoxPicker")!);
            AssertVisible(window, view.FindControl<Button>("ArrangeButton")!);
            var headers = view.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Where(h => h.IsEffectivelyVisible && h.Content is string { Length: > 0 }).ToArray();
            Assert.Equal(4, headers.Length);
            foreach (var header in headers) AssertVisible(window, header);
            Capture(window, $"livingdex-arrange-{language}-{(dark ? "dark" : "light")}");

            // A late-dex species alone in the last box needs far more than one box once gaps are kept.
            var late = save.BlankPKM;
            late.Species = (ushort)save.Personal.MaxSpeciesID;
            late.CurrentLevel = 5;
            late.RefreshChecksum();
            save.SetBoxSlotAtIndex(late, save.BoxCount - 1, 0);
            vm.StartBox = save.BoxCount - 1;
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            AssertVisible(window, view.FindControl<TextBlock>("ArrangeProblem")!);
            Assert.False(vm.ArrangeCommand.CanExecute(null));
            Capture(window, $"livingdex-arrange-{language}-{(dark ? "dark" : "light")}-nofit");
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
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        Assert.Contains(pixels.Pixels, pixel => pixel != pixels.GetPixel(0, 0));
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR") is { } path)
        {
            Directory.CreateDirectory(path);
            bitmap.Save(Path.Combine(path, $"{name}.png"));
        }
    }
}
