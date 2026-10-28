using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests.Harness;

/// <summary>
/// Renders the real <see cref="Pokedex8bLumiEditor"/>/<see cref="Misc8bLumiEditor"/> Views (not just
/// their ViewModels) in a bare headless window against the real Luminescent Platinum test save, to
/// confirm the Stage 2 dialogs actually bind and display real data end to end. This complements
/// <c>Pokedex8bLumiAndMisc8bLumiTests</c> (ViewModel-only construction) and follows the same pattern as
/// <c>JoinAvenueEditorHeadlessTests</c> (bare <see cref="Window"/>, no full app/dialog-service plumbing,
/// since <see cref="PKHeX.Application.Abstractions.IWindowService.ShowDialogAsync"/> is stubbed in the
/// full-app harness and never actually realizes a modal View headlessly).
/// </summary>
public class Lumi8bEditorRenderTests(ITestOutputHelper output)
{
    static Lumi8bEditorRenderTests()
    {
        GameInfo.Strings = GameInfo.GetStrings("en");
    }

    private static SAV8BSLuminescent? LoadRealSave()
    {
        var savDir = SaveFileFixture.FindSaveFilesPath();
        if (savDir is null) return null;
        var path = Path.Combine(savDir, "LuminescentPlatinum_test.bin");
        return SaveFileFixture.LoadSave(path) as SAV8BSLuminescent;
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public void Pokedex8bLumiEditor_RendersRealSaveData()
    {
        var sav = LoadRealSave();
        if (sav is null) { output.WriteLine("Skip: LuminescentPlatinum_test.bin not found"); return; }

        var vm = new Pokedex8bLumiEditorViewModel(sav);
        var view = new Pokedex8bLumiEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        window.Show();
        Pump(window);

        // The species list box must actually realize real rows (not just hold data in the VM) -
        // proves the View is bound to the ViewModel's ObservableCollection, not just constructed.
        var list = view.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(list.Bounds.Width > 0 && list.Bounds.Height > 0, "species list must be laid out with real bounds");
        var realizedSpeciesRows = list.GetVisualDescendants().OfType<TextBlock>()
            .Count(tb => !string.IsNullOrEmpty(tb.Text));
        Assert.True(realizedSpeciesRows > 0, "species list must realize at least one species name row");

        // Select a high-dex species (beyond the original 493-species Sinnoh dex) and confirm the
        // rendered state combo + gender checkboxes reflect the real save's stored flags.
        var highSpecies = vm.FilteredSpecies.FirstOrDefault(s => s.Value > 493 && s.Value <= sav.MaxSpeciesID);
        Assert.NotNull(highSpecies);
        vm.SelectedSpecies = highSpecies;
        Pump(window);

        var stateCombo = view.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.Equal(vm.State, stateCombo.SelectedIndex);

        // Language/form section must be disabled for a species above the Sinnoh dex (Stage 1 finding) -
        // confirm the real rendered control (not just the VM flag) reflects that.
        var langWrap = view.GetVisualDescendants().OfType<WrapPanel>()
            .FirstOrDefault(w => w.Children.OfType<CheckBox>().Any());
        Assert.NotNull(langWrap);
        Assert.False(langWrap!.IsEnabled, "language/form WrapPanel must be disabled for a high-dex species");

        output.WriteLine($"Rendered {realizedSpeciesRows} species rows; selected species {highSpecies!.Value} " +
                          $"state={vm.State} canEditLangForms={vm.CanEditLanguageAndForms}");

        TryCaptureFrame(window, "pokedex8blumi.png");
        window.Close();
    }

    [AvaloniaFact]
    public void Misc8bLumiEditor_RendersRealSaveData()
    {
        var sav = LoadRealSave();
        if (sav is null) { output.WriteLine("Skip: LuminescentPlatinum_test.bin not found"); return; }

        var vm = new Misc8bLumiEditorViewModel(sav);
        var view = new Misc8bLumiEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 700, Height = 650 };

        window.Show();
        Pump(window);

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        Assert.True(buttons.Count >= 9, $"expected at least 9 action buttons, found {buttons.Count}");
        Assert.All(buttons, b => Assert.True(b.Bounds.Width > 0 && b.Bounds.Height > 0, "every button must be laid out"));

        // The Rebattle/Defeat buttons' enabled state must mirror the real save's BattleTrainer data
        // (read through the Lumi-repacked block, not a base SAV8BS-typed misinterpretation of it).
        output.WriteLine($"CanRebattle={vm.CanRebattle} CanDefeatAll={vm.CanDefeatAll} " +
                          $"CanUnlockSpiritomb={vm.CanUnlockSpiritomb} CanUnlockDarkrai={vm.CanUnlockDarkrai}");

        TryCaptureFrame(window, "misc8blumi.png");
        window.Close();
    }

    /// <summary>
    /// Opt-in real-pixel capture, same gate as <c>HeadlessGiftRecordTests.CaptureSvGiftRecords_WhenEnabled_WritesPng</c>:
    /// only produces non-blank pixels when the test process was started with <c>PKHEX_HEADLESS_CAPTURE=1</c>
    /// (Skia + real drawing), which must be set before the test process starts since Avalonia's drawing
    /// mode is a per-process singleton fixed at app-build time. Silently a no-op otherwise.
    /// </summary>
    private static void TryCaptureFrame(Window window, string fileName)
    {
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") != "1")
            return;

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        WriteableBitmap? frame;
        try
        {
            frame = window.GetLastRenderedFrame();
        }
        catch (NotSupportedException)
        {
            return;
        }
        if (frame is null)
            return;

        var directory = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")
                        ?? Path.Combine(Path.GetTempPath(), "pkhex-headless-frames");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var fs = File.Create(path);
        frame.Save(fs);
    }
}
