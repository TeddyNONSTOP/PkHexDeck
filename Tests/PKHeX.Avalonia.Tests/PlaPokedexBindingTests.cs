using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;
using System.Globalization;

namespace PKHeX.Avalonia.Tests;

public class PlaPokedexBindingTests
{
    [AvaloniaFact]
    public void UnknownDisplayedFormAndNaNSizePayloadSurviveARealizedNoOp()
    {
        var save = PlaPokedexFoundationTests.LoadSave(); var dex = save.Blocks.PokedexSave;
        dex.SetSelectedGenderForm(201, 63, false, false, false);
        float payload = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234)); dex.SetSizeStatistics(201, 0, true, payload, payload, 1, 2);
        var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        using var vm = new PokedexLAEditorViewModel(save); vm.SelectedSpecies = vm.SpeciesList.Single(entry => entry.Species == 201);
        var view = new PokedexLAEditor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 420 };
        using var lifetime = new HeadlessWindowLifetime(window); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.Equal(63, vm.SelectedSpecies.DisplayForm); vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
    }
    [AvaloniaFact]
    public void LanguageSwitchRetainsPendingSizesTaskCountsAndDisplayedForm()
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        var dataLanguage = GameInfo.CurrentLanguage; var strings = GameInfo.Strings; var filtered = GameInfo.FilteredSources;
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture; var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var save = PlaPokedexFoundationTests.LoadSave(); using var vm = new PokedexLAEditorViewModel(save);
        var view = new PokedexLAEditor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 420 };
        using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            new LanguageService().SetLanguage("de"); LocalizedStrings.Instance.SetLanguage("de"); vm.RefreshLanguage();
            var unown = vm.SpeciesList.Single(entry => entry.Species == 201); vm.SelectedSpecies = unown; unown.DisplayForm = 3;
            unown.SelectedForm = unown.Forms.First(form => form.Form == 3); unown.SelectedForm.HasMaximum = true;
            unown.SelectedForm.MinimumHeight = "1,5"; unown.SelectedForm.MaximumHeight = "2,5"; unown.Tasks[0].CurrentValue = 2;
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            new LanguageService().SetLanguage("en"); LocalizedStrings.Instance.SetLanguage("en"); vm.RefreshLanguage();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(unown, vm.SelectedSpecies); Assert.Equal(3, unown.DisplayForm); Assert.Equal(3, unown.SelectedForm.Form);
            Assert.Equal("1.5", unown.SelectedForm.MinimumHeight); Assert.Equal("2.5", unown.SelectedForm.MaximumHeight);
            Assert.Equal(2, unown.Tasks[0].CurrentValue); Assert.True(unown.IsValid);
        }
        finally
        {
            LocalizedStrings.Instance.SetLanguage(previous); GameInfo.CurrentLanguage = dataLanguage;
            GameInfo.Strings = strings; GameInfo.FilteredSources = filtered;
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture; CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }
    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Languages))]
    public void EveryLocaleAndEntryKeepsApplyReachableAndNoOpBytesExact(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        var dataLanguage = GameInfo.CurrentLanguage; var strings = GameInfo.Strings; var filtered = GameInfo.FilteredSources;
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture; var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var save = PlaPokedexFoundationTests.LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new PokedexLAEditorViewModel(save); var view = new PokedexLAEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            new LanguageService().SetLanguage(language); LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            foreach (ushort species in new ushort[] { 722, 25, 201, 493 })
            {
                vm.SelectedSpecies = vm.SpeciesList.Single(entry => entry.Species == species);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var apply = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveCommand));
                var point = apply.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
                Assert.All(vm.SelectedSpecies.Tasks, task => Assert.False(string.IsNullOrWhiteSpace(task.Description)));
            }
            vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
            foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        }
        finally
        {
            LocalizedStrings.Instance.SetLanguage(previous); GameInfo.CurrentLanguage = dataLanguage;
            GameInfo.Strings = strings; GameInfo.FilteredSources = filtered;
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture; CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }
}
