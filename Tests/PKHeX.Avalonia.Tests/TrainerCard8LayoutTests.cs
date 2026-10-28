using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class TrainerCard8LayoutTests
{
    [AvaloniaFact]
    public void LiveLanguageRoundtripPreservesUnknownSelectionsAndStagedEdits()
    {
        var oldUi = LocalizedStrings.Instance.CurrentLanguage; var oldData = GameInfo.CurrentLanguage;
        var oldStrings = GameInfo.Strings; var oldFiltered = GameInfo.FilteredSources;
        var oldCulture = System.Globalization.CultureInfo.CurrentCulture; var oldUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var oldDefault = System.Globalization.CultureInfo.DefaultThreadCurrentCulture; var oldDefaultUi = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        using var app = new HeadlessAppFixture(); Window? window = null;
        try
        {
            var save = TrainerCard8WorkflowTests.CreateSave(); Seed(save, false);
            var before = TrainerCard8WorkflowTests.Snapshot(save);
            using var vm = new TrainerCard8EditorViewModel(save, app.Services.GetRequiredService<ISpriteRenderer>());
            vm.TrainerName = "Changed"; vm.CardTeam[1].Species = 133;
            var view = new TrainerCard8EditorView { DataContext = vm }; window = new Window { Content = view, Width = 600, Height = 420 };
            window.Show(); view.FindControl<TabControl>("Card8Tabs")!.SelectedIndex = 1; Pump(window);
            foreach (var language in new[] { "de", "ja", "en" })
            {
                app.ViewModel.LanguageService.SetLanguage(language); Pump(window);
                Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);
                Assert.Equal(255, vm.Starter); Assert.Equal(ushort.MaxValue, vm.CardTeam[0].Species);
                Assert.Equal(253, vm.CardTeam[0].Gender); Assert.Equal("Changed", vm.TrainerName);
                Assert.Contains(GameInfo.Strings.Species[133], vm.CardTeam[1].Summary);
                Assert.True(vm.CanSave); TrainerCard8WorkflowTests.AssertUnchanged(save, before);
            }
        }
        finally
        {
            window?.Close(); LocalizedStrings.Instance.SetLanguage(oldUi);
            GameInfo.CurrentLanguage = oldData; GameInfo.Strings = oldStrings; GameInfo.FilteredSources = oldFiltered;
            System.Globalization.CultureInfo.CurrentCulture = oldCulture; System.Globalization.CultureInfo.CurrentUICulture = oldUiCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = oldDefault; System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = oldDefaultUi;
        }
    }

    public static IEnumerable<object[]> Cases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { 0, 2 }.Select(revision => new object[] { language, revision }));
    internal static void Seed(SAV8SWSH save, bool populated)
    {
        save.TrainerCard.Starter = 255;
        save.TrainerCard.OT = "Trainer";
        if (populated)
        {
            save.TrainerCard.ViewPoke(0).Species = 25;
            save.TrainerCard.ViewPoke(0).IsShiny = true;
            save.TrainerCard.ViewPoke(0).FormArgument = -1;
            save.TitleScreen.ViewPoke(0).Species = 133;
            save.TitleScreen.ViewPoke(0).Gender = 1;
            save.TitleScreen.ViewPoke(0).FormArgument = -1;
        }
        else
        {
            save.TrainerCard.PokeDexOwned = ushort.MaxValue;
            save.TrainerCard.ShinyPokemonFound = ushort.MaxValue;
            save.TrainerCard.CaughtPokemon = -1;
            save.TrainerCard.CurryTypesOwned = ushort.MaxValue;
            save.TrainerCard.ViewPoke(0).Species = ushort.MaxValue;
            save.TrainerCard.ViewPoke(0).Gender = 253;
            save.TrainerCard.ViewPoke(0).Form = 255;
        }
        save.TrainerCard.ViewPoke(0).Unknown = 0xA5B6C7D8;
        save.TitleScreen.ViewPoke(0).Unknown18 = 0x12345678;
        save.TrainerCard.Data[TrainerCard8.GetPokeOffset(0) + 2] = 0xA5;
        save.State.Edited = false;
    }
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task AllLocalesBaseAndCrown_RenderUnknownSelectionsAndNoOpSaveWithoutMutation(string language, int revision)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture();
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var save = TrainerCard8WorkflowTests.CreateSave(revision, revision == 0 ? GameVersion.SW : GameVersion.SH);
            Seed(save, revision == 2); var before = TrainerCard8WorkflowTests.Snapshot(save);
            var sprites = app.Services.GetRequiredService<ISpriteRenderer>(); sprites.Initialize(save);
            using var vm = new TrainerCard8EditorViewModel(save, sprites);
            var view = new TrainerCard8EditorView { DataContext = vm };
            window = new Window { Content = view, Width = 600, Height = 420 }; window.Show(); Pump(window);
            Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);
            Assert.Equal(255, vm.Starter);
            Assert.Equal(255, view.FindControl<ComboBox>("Card8StarterChoice")!.SelectedValue);
            var tabs = view.FindControl<TabControl>("Card8Tabs")!;
            tabs.SelectedIndex = 1; Pump(window);
            Assert.Equal(revision == 2 ? 25 : ushort.MaxValue, vm.CardTeam[0].Species);
            Assert.Equal(revision == 2 ? 0 : 253, vm.CardTeam[0].Gender);
            var species = view.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Name == "TeamSpeciesChoice");
            Assert.Equal(vm.CardTeam[0].Species, species.SelectedValue);
            if (revision == 2) Assert.NotNull(vm.CardTeam[0].Sprite);
            var detail = view.GetVisualDescendants().OfType<ScrollViewer>().Single(scroll => scroll.Name == "TeamDetailScroll");
            detail.ScrollToEnd(); Pump(window);
            Assert.True(detail.Viewport.Height > 60);
            tabs.SelectedIndex = 2; Pump(window);
            Assert.Equal(revision == 2 ? 133 : 0, vm.TitleTeam[0].Species);
            tabs.SelectedIndex = 3; Pump(window);
            view.FindControl<ScrollViewer>("Card8AppearanceScroll")!.ScrollToEnd(); Pump(window);
            var saveButton = view.FindControl<Button>("Card8SaveButton")!;
            var point = saveButton.TranslatePoint(default, view)!.Value;
            Assert.True(point.X >= 0 && point.X + saveButton.Bounds.Width <= view.Bounds.Width);
            Assert.True(point.Y >= 0 && point.Y + saveButton.Bounds.Height <= view.Bounds.Height);
            Assert.True(vm.CanSave);
            await vm.SaveCommand.ExecuteAsync(null);
            TrainerCard8WorkflowTests.AssertUnchanged(save, before);
            Assert.False(save.State.Edited);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }

    [AvaloniaFact]
    public async Task RealMenuSuppliesSpriteRendererAndDialogCloseContract()
    {
        using var app = new HeadlessAppFixture();
        var save = new SAV8SWSH { Version = GameVersion.SW };
        save.TrainerCard.ViewPoke(0).Species = 25;
        app.LoadSaveInstance(save);
        await app.ViewModel.OpenTrainerCard8Command.ExecuteAsync(null);
        using var vm = Assert.IsType<TrainerCard8EditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.IsAssignableFrom<ICloseableDialog>(vm);
        Assert.NotNull(vm.CardTeam[0].Sprite);
        bool closed = false; vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.False(vm.CanSave);
    }
}
