using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class TrainerImageImportBindingTests
{
    public static IEnumerable<object[]> Locales => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Locales))]
    public void EveryLocaleBindsThreeImportActionsAndKeepsApplyReachable(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var vm = new TrainerEditorViewModel(TrainerImageImportTests.Save(), imageCodec: new PngImageCodec());
        var view = new TrainerZaWorkspace { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 420 };
        using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage();
            view.FindControl<TabControl>("TrainerZaSections")!.SelectedIndex = 2;
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var imports = view.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.Command, vm.ImportZaImageCommand)).ToArray();
            Assert.Equal(3, imports.Length);
            foreach (var button in imports)
            {
                var image = Assert.IsType<ZaTrainerImageViewModel>(button.CommandParameter);
                Assert.True(image.CanImport); Assert.True(button.IsEnabled);
                Assert.Equal(LocalizedStrings.Instance["TrainerZA_ImportImage"], button.Content);
            }
            var apply = view.FindControl<Button>("TrainerZaApply")!; var point = apply.TranslatePoint(default, view)!.Value;
            Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
