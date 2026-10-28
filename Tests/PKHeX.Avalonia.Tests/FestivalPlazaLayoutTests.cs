using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class FestivalPlazaLayoutTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    public static IEnumerable<object[]> LayoutCases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { "gen7_sun.main", "gen7_ultrasun.main" }.Select(file => new object[] { file, language, language == "en" ? 740 : 480, language == "en" ? 620 : 360 }));
    [AvaloniaTheory]
    [MemberData(nameof(LayoutCases))]
    public void AllTabsAndSelectors_RealizeWithoutMutatingFixture_AndFooterRemainsReachable(string file, string language, int width, int height)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var directory = SaveFileFixture.FindSaveFilesPath()!;
            var save = Assert.IsAssignableFrom<SAV7>(SaveFileFixture.LoadSave(Path.Combine(directory, file)));
            save.Festa.GetFestaFacility(0).Type = 255; save.Festa.GetFestaFacility(0).Color = 255;
            save.Festa.SetFestaPrizeReceived(0, 255); save.State.Edited = false;
            var before = save.Data.ToArray(); using var vm = new FestivalPlazaEditorViewModel(save);
            var view = new FestivalPlazaEditor { DataContext = vm };
            window = new Window { Content = view, Width = width, Height = height }; window.Show(); Pump(window);
            var tabs = view.FindControl<TabControl>("PlazaTabs")!;
            for (var tab = 0; tab < 3; tab++)
            {
                tabs.SelectedIndex = tab; Pump(window);
                Assert.True(tabs.Bounds.Height > 100);
                foreach (var name in new[] { "SaveButton", "CancelButton", "ResetButton" })
                {
                    var button = view.FindControl<Button>(name)!;
                    var point = button.TranslatePoint(default, view)!.Value;
                    Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= height);
                    Assert.True(button.Bounds.Width > 25);
                }
            }
            var type = view.FindControl<ComboBox>("FacilityTypeSelector")!;
            var color = view.FindControl<ComboBox>("FacilityColorSelector")!;
            Assert.Equal(255, type.SelectedValue); Assert.Equal(255, color.SelectedValue);
            Assert.NotNull(type.SelectionBoxItem); Assert.NotNull(color.SelectionBoxItem);
            var scroll = view.FindControl<ScrollViewer>("FacilityScroll")!;
            scroll.ScrollToEnd(); Pump(window);
            var finalUsage = view.FindControl<ItemsControl>("UsageFields")!.GetVisualDescendants().OfType<NumericUpDown>().Last();
            var usagePoint = finalUsage.TranslatePoint(default, scroll)!.Value;
            Assert.True(usagePoint.Y >= 0 && usagePoint.Y + finalUsage.Bounds.Height <= scroll.Bounds.Height);
            vm.SaveCommand.Execute(null);
            Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [AvaloniaTheory]
    [InlineData("gen7_sun.main")] [InlineData("gen7_ultrasun.main")]
    public async Task Menu_OpensStagedWorkspace_WithConfirmationService(string file)
    {
        using var app = new HeadlessAppFixture();
        var directory = SaveFileFixture.FindSaveFilesPath()!;
        var save = Assert.IsAssignableFrom<SAV7>(SaveFileFixture.LoadSave(Path.Combine(directory, file)));
        app.LoadSaveInstance(save);
        await app.ViewModel.OpenFestivalPlazaCommand.ExecuteAsync(null);
        using var vm = Assert.IsType<FestivalPlazaEditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.True(vm.CanBulk);
        Assert.IsType<FestivalPlazaEditor>(global::PKHeX.Avalonia.ViewLocator.Build(vm));
        var before = save.Data.ToArray(); vm.PlazaName = "Discarded";
        vm.CancelCommand.Execute(null); vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Data.ToArray());
    }

    [AvaloniaFact]
    public void FacilitySelectorAndFestivalId_AreTwoWay_AndInvalidInputDisablesSave()
    {
        var save = new SAV7USUM(); using var vm = new FestivalPlazaEditorViewModel(save) { SelectedTab = 2 };
        var view = new FestivalPlazaEditor { DataContext = vm }; var window = new Window { Content = view, Width = 740, Height = 620 };
        window.Show();
        try
        {
            Pump(window); var type = view.FindControl<ComboBox>("FacilityTypeSelector")!;
            type.SelectedValue = 127; Pump(window); Assert.Equal(127, vm.SelectedFacility!.Type);
            view.FindControl<TextBox>("FestivalIdInput")!.Text = "bad"; Pump(window);
            Assert.False(view.FindControl<Button>("SaveButton")!.IsEffectivelyEnabled);
            view.FindControl<TextBox>("FestivalIdInput")!.Text = "ABCDEF0123456789ABCDEF01"; Pump(window);
            Assert.True(view.FindControl<Button>("SaveButton")!.IsEffectivelyEnabled);
            vm.SaveCommand.Execute(null);
            Assert.Equal(127, save.Festa.GetFestaFacility(0).Type);
            Assert.Equal("ABCDEF0123456789ABCDEF01", Convert.ToHexString(save.Festa.GetFestaFacility(0).TrainerFesID));
        }
        finally { window.Close(); }
    }
}
