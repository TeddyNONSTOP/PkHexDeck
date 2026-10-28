using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PKHeX.Application.Services;
using PKHeX.Application.UseCases;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Render.Tests;

public class TransferPreviewRenderTests
{
    private static PKM Gen3Pikachu(string nickname)
    {
        var pk = new PK3
        {
            Species = 25,
            CurrentLevel = 20,
            Version = GameVersion.E,
            Language = (int)LanguageID.English,
            OriginalTrainerName = "MAY",
            TID16 = 12345,
            SID16 = 54321,
            MetLocation = 20,
            MetLevel = 5,
            Ball = (byte)Ball.Poke,
            Move1 = 84,
            HeldItem = 13,
        };
        pk.Nickname = nickname;
        pk.IsNicknamed = true;
        pk.SetUnshiny();
        pk.RefreshChecksum();
        return pk;
    }

    [AvaloniaTheory]
    [InlineData("en", false, 1)] [InlineData("en", true, 2)]
    [InlineData("de", false, 2)] [InlineData("ja", true, 1)]
    public async Task PreviewShowsChangesLegalityAndActions(string language, bool dark, int count)
    {
        var previousLanguage = LocalizedStrings.Instance.CurrentLanguage;
        var application = global::Avalonia.Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var save = BlankSaveFile.Get(GameVersion.Pt);
            var useCase = new EntityTransferPreviewUseCase();
            var previews = Enumerable.Range(0, count).Select(i =>
            {
                var source = Gen3Pikachu(i == 0 ? "SPARKY" : "VOLT");
                return useCase.Build(save, source, BankEntityConversion.ConvertForSave(save, source).Entity!);
            }).ToArray();
            var renderer = new AvaloniaSpriteRenderer(new AppSettings());
            renderer.Initialize(save);
            var vm = new TransferPreviewViewModel(previews, "Platinum (Test)", LocalizedStrings.Instance["TransferPreview_Withdraw"], renderer);
            Assert.NotEmpty(vm.Items[0].Changes);

            var view = new TransferPreviewView { DataContext = vm };
            window = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight };
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            AssertVisible(window, view.FindControl<TextBlock>("PreviewSummary")!);
            AssertVisible(window, view.FindControl<TextBlock>("PreviewName")!);
            AssertVisible(window, view.FindControl<Grid>("ChangesTable")!);
            AssertVisible(window, view.FindControl<Button>("ConfirmButton")!);
            AssertVisible(window, view.FindControl<Button>("CancelButton")!);
            Assert.Equal(count > 1, view.FindControl<ListBox>("PreviewList")!.IsEffectivelyVisible);
            var legal = view.FindControl<TextBlock>("PreviewLegal")!;
            var illegal = view.FindControl<TextBlock>("PreviewIllegal")!;
            Assert.NotEqual(legal.IsEffectivelyVisible, illegal.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);
            Capture(window, $"transfer-preview-{language}-{(dark ? "dark" : "light")}-{count}");
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
        Assert.True(control.IsEffectivelyVisible, $"{control.Name} is hidden");
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
            point.Y + control.Bounds.Height <= window.ClientSize.Height + 1,
            $"{control.Name} at {point} + {control.Bounds.Size} outside {window.ClientSize}");
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
