using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using PKHeX.Avalonia.Views;

namespace PKHeX.Avalonia.Render.Tests;

/// <summary>
/// The Batch tab in the composed app: typing an instruction enables Run Batch, and pressing it edits
/// the save. Covers Luminescent Platinum, whose Pokémon Core's batch editor once could not read.
/// </summary>
public class BatchEditorRunRenderTests
{
    [AvaloniaFact]
    public async Task TypingAnInstruction_EnablesRunBatch_AndRunningEditsTheSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "pkhex-render-" + Guid.NewGuid().ToString("N"));
        var paths = new Mock<IAppPaths>();
        paths.SetupGet(x => x.ConfigDirectory).Returns(Path.Combine(root, "config"));
        paths.SetupGet(x => x.DataDirectory).Returns(Path.Combine(root, "data"));
        paths.SetupGet(x => x.ConfigFilePath).Returns(Path.Combine(root, "config.json"));
        paths.SetupGet(x => x.LegacyConfigFilePath).Returns(Path.Combine(root, "legacy.json"));
        using var services = (ServiceProvider)App.BuildServiceProvider(paths.Object, Mock.Of<ISettingsStore>(), new AppSettings(), s =>
        {
            s.AddSingleton(Mock.Of<IDialogService>());
            s.AddSingleton(Mock.Of<IWindowService>());
        });
        var vm = services.GetRequiredService<MainWindowViewModel>();
        Window? window = null;
        try
        {
            // PKHEX_BATCH_SAVE points the test at another save (a local copy) when chasing a report.
            var path = Environment.GetEnvironmentVariable("PKHEX_BATCH_SAVE");
            if (string.IsNullOrEmpty(path))
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tests/savefiles/LuminescentPlatinum_test.bin"))) directory = directory.Parent;
                Assert.NotNull(directory);
                path = Path.Combine(directory.FullName, "Tests/savefiles/LuminescentPlatinum_test.bin");
            }
            var save = Assert.IsAssignableFrom<SaveFile>(FileUtil.GetSupportedFile(File.ReadAllBytes(path), path));
            services.GetRequiredService<ISaveFileGateway>().OpenLoadedSave(save, "fixture.bin");
            var size = (Environment.GetEnvironmentVariable("PKHEX_BATCH_SIZE") ?? "900x600").Split('x').Select(double.Parse).ToArray();
            window = new MainWindow { DataContext = vm, Width = size[0], Height = size[1] }; window.Show();
            vm.SelectWorkspaceTabCommand.Execute(MainWindowViewModel.BatchWorkspaceTabIndex);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);

            var editor = window.GetVisualDescendants().OfType<BatchEditor>().Single();
            if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR") is { } capture)
            {
                Directory.CreateDirectory(capture);
                using var shot = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)size[0], (int)size[1]));
                shot.Render(window);
                shot.Save(Path.Combine(capture, $"batch-{size[0]}x{size[1]}.png"));
                foreach (var t in editor.GetVisualDescendants().OfType<TextBox>())
                    Console.WriteLine($"TextBox ro={t.IsReadOnly} multi={t.AcceptsReturn} bounds={t.Bounds} visible={t.IsEffectivelyVisible} inWindow={t.TranslatePoint(new global::Avalonia.Point(0, 0), window)}");
            }
            var instructions = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn && !t.IsReadOnly);
            var run = editor.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BatchEditor!.RunBatchCommand));
            Assert.False(run.IsEffectivelyEnabled);
            // At the default 900x600 the page scrolls rather than squeezing the box to one line.
            Assert.True(instructions.Bounds.Height >= 140, $"Instructions box is {instructions.Bounds.Height}px tall");

            instructions.Text = ".Nickname=HCTEST";
            for (var i = 0; i < 200 && !run.IsEffectivelyEnabled; i++)
            {
                await Task.Delay(25);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(vm.BatchEditor!.AffectedCount > 0, $"Affected count stayed {vm.BatchEditor.AffectedCount}");
            Assert.True(run.IsEffectivelyEnabled);

            run.Command!.Execute(null);
            for (var i = 0; i < 200 && vm.BatchEditor.IsRunning; i++)
            {
                await Task.Delay(25);
                Dispatcher.UIThread.RunJobs();
            }

            var first = Enumerable.Range(0, save.BoxCount * save.BoxSlotCount)
                .Select(i => save.GetBoxSlotAtIndex(i)).First(pk => pk.Species != 0);
            Assert.Equal("HCTEST", first.Nickname);
        }
        finally
        {
            services.GetRequiredService<ISaveFileGateway>().CloseSave();
            window?.Close();
        }
    }
}
