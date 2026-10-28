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

public class BdspTrainerBindingTests
{
    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Languages))]
    public void EveryLocaleRetainsFixtureBytesAcrossAllTabsAtCompactSize(string language)
    {
        string previous = LocalizedStrings.Instance.CurrentLanguage;
        var save = Assert.IsType<SAV8BS>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8b_brilliantdiamond.bin")));
        var before = save.Data.ToArray(); using var vm = new BdspTrainerEditorViewModel(save);
        var view = new BdspTrainerEditor { DataContext = vm }; var window = new Window { Content = view, Width = 620, Height = 440 };
        using var lifetime = new HeadlessWindowLifetime(window);
        try
        {
            LocalizedStrings.Instance.SetLanguage(language); vm.RefreshLanguage(); window.Show();
            var tabs = view.FindControl<TabControl>("BdspTrainerSections")!;
            for (int index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var apply = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveCommand));
                var point = apply.TranslatePoint(default, view)!.Value; Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            }
            vm.SaveCommand.Execute(null); Assert.Empty(vm.Error); Assert.Equal(before, save.Data.ToArray());
        }
        finally { LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [Fact]
    public void ParentApplyPreservesDedicatedDraftAndCloseDisposesTheWriter()
    {
        var save = Assert.IsType<SAV8BS>(Fixtures.SaveFileFixture.LoadSave(Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8b_brilliantdiamond.bin")));
        using var vm = new TrainerEditorViewModel(save); Assert.True(vm.IsBDSP);
        vm.BdspEditor!.RivalName = "Staged"; vm.Money = 123; vm.SaveCommand.Execute(null);
        Assert.Equal("Staged", save.RivalName); Assert.Equal(123u, save.Money);
        var before = save.Data.ToArray(); vm.BdspEditor.RivalName = "Discard"; vm.Dispose(); vm.BdspEditor.SaveCommand.Execute(null); Assert.Equal(before, save.Data.ToArray());
    }
}
