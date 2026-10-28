using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;
using PKHeX.Avalonia.Views;

namespace PKHeX.Avalonia.Tests;

public class Gen7TrainerBindingTests
{
    public static IEnumerable<object[]> Scopes => LocalizedStrings.SupportedLanguages.SelectMany(language => new[] { "gen7_sun.main", "gen7_ultrasun.main" }.Select(file => new object[] { language, file }));
    [AvaloniaTheory] [MemberData(nameof(Scopes))]
    public void AllTabsRealizeWithoutNormalizingSourceAndApplyRemainsVisible(string language, string file)
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = Assert.IsAssignableFrom<SAV7>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, file)));
        var before = save.Data.ToArray();
        using var vm = new Misc7EditorViewModel(save); var view = new Misc7Editor { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            var tabs = view.FindControl<TabControl>("Trainer7Sections")!;
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i; Pump(window);
                var apply = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveCommand));
                var point = apply.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            }
            vm.SaveCommand.Execute(null); Assert.False(vm.HasError, vm.Error); Assert.Equal(before, save.Data.ToArray());
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }

    [AvaloniaFact]
    public void LanguageChangeKeepsSelectionsAndPendingFlags()
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        using var vm = new Misc7EditorViewModel(new SAV7USUM());
        vm.Country = 255; vm.SubRegion = 255; vm.ConsoleRegion = 255; vm.Language = 255; vm.Gender = 255;
        vm.SkinColor = 7; vm.BattleStyle = 250; vm.AlolaOffset = ulong.MaxValue; vm.CameraVersion = 65000;
        vm.Stamps[0].IsObtained = true; vm.MapUnmask[0].IsUnlocked = true; vm.FlyDestinations[0].IsUnlocked = true;
        var view = new Misc7Editor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            window.Show(); Pump(window); LocalizedStrings.Instance.SetLanguage("de"); vm.RefreshLanguage(); Pump(window);
            Assert.Equal(255, vm.Country); Assert.Equal(255, vm.SubRegion); Assert.Equal(255, vm.ConsoleRegion);
            Assert.Equal(255, vm.Language); Assert.Equal(255, vm.Gender); Assert.Equal(65000, vm.CameraVersion);
            Assert.Equal(ulong.MaxValue, vm.AlolaOffset); Assert.Equal(250, vm.BattleStyle); Assert.Equal(7, vm.SkinColor);
            Assert.True(vm.Stamps[0].IsObtained); Assert.True(vm.MapUnmask[0].IsUnlocked); Assert.True(vm.FlyDestinations[0].IsUnlocked);
            Assert.DoesNotContain("OfficialPokemonTrainer", vm.Stamps[0].Name);
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
