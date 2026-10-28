using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class PlaTrainerBindingTests
{
    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Languages))]
    public void EveryLocaleKeepsSatchelFortyAndApplyReachableOnFirstLayout(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = PlaTrainerFieldTests.LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new Misc8aEditorViewModel(save); var view = new Misc8aEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 420 }; using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            var tabs = view.FindControl<TabControl>("TrainerPlaSections")!;
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var apply = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveCommand));
                var point = apply.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            }
            Assert.Equal(40u, vm.SatchelUpgrades); vm.SaveCommand.Execute(null); Assert.False(vm.HasError, vm.Error);
            foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
