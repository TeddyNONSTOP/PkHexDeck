using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class SvPokedexBindingTests
{
    public static IEnumerable<object[]> Scopes => LocalizedStrings.SupportedLanguages.SelectMany(language => new[] { "gen9_scarlet.main", "gen9_scarlet_teal_public.main", "gen9_violet_indigo_public.main" }.Select(file => new object[] { language, file }));
    [AvaloniaTheory] [MemberData(nameof(Scopes))]
    public void EveryLocaleAndFormatPreservesRealizedNoOpBytes(string language, string file)
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = Assert.IsType<SAV9SV>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, file)));
        var before = save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());
        using var vm = new PokedexGen9EditorViewModel(save); var view = new PokedexGen9Editor { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 440 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            foreach (int species in vm.UsesDlcFormat ? (save.SaveRevision == 1 ? new[] { 25, 1017 } : new[] { 25, 1017, 1024, 1025 }) : new[] { 25, 906, 1010 })
            {
                vm.SelectedSpecies = vm.SpeciesList.Single(choice => choice.Value == species); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var apply = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveCurrentCommand));
                var point = apply.TranslatePoint(default, view)!.Value; Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            }
            vm.SaveCurrentCommand.Execute(null); Assert.Empty(vm.Error);
            foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [AvaloniaFact]
    public void LanguageChangeKeepsPendingRegionalAndLegacyDrafts()
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = Assert.IsType<SAV9SV>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen9_violet_indigo_public.main")));
        using var vm = new PokedexGen9EditorViewModel(save); vm.SelectedSpecies = vm.SpeciesList.Single(choice => choice.Value == 1017);
        var display = vm.RegionalDisplays[0]; display.Gender = 255; vm.FormStates[0].Viewed = !vm.FormStates[0].Viewed; bool viewed = vm.FormStates[0].Viewed;
        var view = new PokedexGen9Editor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 440 };
        using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            window.Show(); LocalizedStrings.Instance.SetLanguage("de"); vm.RefreshLanguage(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(1017, vm.SelectedSpecies!.Value); Assert.Equal(255, display.Gender); Assert.Equal(viewed, vm.FormStates[0].Viewed); Assert.False(vm.CanSave);
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
