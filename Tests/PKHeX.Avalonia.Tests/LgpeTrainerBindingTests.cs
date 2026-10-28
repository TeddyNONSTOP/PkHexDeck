using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using PKHeX.Presentation.Localization;

namespace PKHeX.Avalonia.Tests;

public class LgpeTrainerBindingTests
{
    public static IEnumerable<object[]> Locales => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Locales))]
    public async Task BothTrainerWorkspaceAndMiscMenuUseTheCompleteStagedEditor(string language)
    {
        using var app = new HeadlessAppFixture(); var save = LgpeTrainerWorkflowTests.CreateSave();
        string previous = LocalizedStrings.Instance.CurrentLanguage; LocalizedStrings.Instance.SetLanguage(language);
        var before = save.Data.ToArray(); app.LoadSaveInstance(save);
        var editor = app.ViewModel.TrainerEditor!;
        Assert.True(editor.IsLGPE); Assert.NotNull(editor.LgpeEditor);
        await app.ViewModel.OpenMisc7bCommand.ExecuteAsync(null);
        using var dialog = Assert.IsType<Misc7bEditorViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);
        Assert.IsType<Misc7bEditor>(ViewLocator.Build(dialog));
        var host = new TrainerEditor { DataContext = editor };
        var window = new Window { Content = host, Width = 620, Height = 420 }; window.Show();
        try
        {
            Pump(window);
            var view = Assert.Single(host.GetVisualDescendants().OfType<Misc7bEditor>());
            var tabs = view.FindControl<TabControl>("LgpeTrainerSections")!; Assert.Equal(3, tabs.Items.Count);
            tabs.SelectedIndex = 2; Pump(window);
            var grid = view.FindControl<DataGrid>("LgpeParkSlots")!;
            Assert.Equal(50, editor.LgpeEditor.ParkSlots.Count);
            editor.LgpeEditor.SelectedArea = 1; Pump(window);
            Assert.Equal(1, editor.LgpeEditor.SelectedArea);
            Assert.Equal(50, editor.LgpeEditor.ParkSlots[0].Index);
            editor.LgpeEditor.RefreshLanguage(); Pump(window);
            Assert.Equal(1, editor.LgpeEditor.SelectedArea);
            Assert.True(grid.Bounds.Height >= 80, $"Grid height {grid.Bounds.Height}");
            var apply = view.FindControl<Button>("LgpeTrainerApply")!; var point = apply.TranslatePoint(default, view)!.Value;
            Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            Assert.True(editor.LgpeEditor.CanSave); editor.LgpeEditor.SaveCommand.Execute(null);
            Assert.Equal(before, save.Data.ToArray());
        }
        finally { window.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
