using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;
using PKHeX.Avalonia.Views;

namespace PKHeX.Avalonia.Tests;

public class SwshTrainerBindingTests
{
    public static IEnumerable<object[]> Scopes => LocalizedStrings.SupportedLanguages.SelectMany(language => new[] { 0, 1, 2 }.Select(revision => new object[] { language, revision }));
    [AvaloniaTheory] [MemberData(nameof(Scopes))]
    public void RevisionAndLocaleTabsKeepApplyReachableAndDoNotNormalizeTheSave(string language, int revision)
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = SwshTrainerActionTests.Revision(revision); var before = save.AllBlocks.ToDictionary(b => b.Key, b => (b.Type, Data: b.Data.ToArray()));
        using var vm = new Misc8EditorViewModel(save); var view = new Misc8Editor { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            var tabs = view.FindControl<TabControl>("Trainer8Sections")!;
            for (int index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index; Pump(window);
                var apply = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.SaveCommand));
                var point = apply.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            }
            vm.SaveCommand.Execute(null); Assert.False(vm.HasError, vm.Error);
            foreach (var b in save.AllBlocks) { Assert.Equal(before[b.Key].Type, b.Type); Assert.Equal(before[b.Key].Data, b.Data.ToArray()); }
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [AvaloniaFact]
    public void BoundLanguageChangesRetainUnknownOptionsAndPendingFields()
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage; using var vm = new Misc8EditorViewModel(SwshTrainerActionTests.Revision(2));
        vm.Gender = 255; vm.Language = 255; vm.SkinColor = -1; vm.LeagueCardName = "Draft";
        var view = new Misc8Editor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            window.Show(); Pump(window); LocalizedStrings.Instance.SetLanguage("de"); vm.RefreshLanguage(); Pump(window);
            Assert.Equal(255, vm.Gender); Assert.Equal(255, vm.Language); Assert.Equal(-1, vm.SkinColor); Assert.Equal("Draft", vm.LeagueCardName);
            Assert.DoesNotContain('_', vm.TrainerRecords[0].Name);
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
