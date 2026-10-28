using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PKHeX.Presentation.Localization;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Pokedex5BulkTests
{
    private static SAV5 Load(string file) => Assert.IsAssignableFrom<SAV5>(SaveFileFixture.LoadSave(
        Path.Combine(SaveFileFixture.FindSaveFilesPath()!, file)));
    [Theory]
    [InlineData("gen5_black.sav")]
    [InlineData("gen5_white2.sav")]
    public void WholeDexCaughtAndClearAreStagedUntilSaveAndResetDiscards(string file)
    {
        var save = Load(file);
        var before = save.Zukan.Data.ToArray();
        var vm = new Pokedex5EditorViewModel(save);
        vm.CaughtAllCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        vm.ResetCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        vm.CaughtAllCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        for (ushort species = 1; species <= save.MaxSpeciesID; species++)
        {
            Assert.True(save.Zukan.GetCaught(species));
            Assert.True(save.Zukan.GetSeen(species));
        }
        var caughtBytes = save.Zukan.Data.ToArray();
        vm.ClearEntriesCommand.Execute(null);
        Assert.Equal(caughtBytes, save.Zukan.Data.ToArray());
        vm.SaveCommand.Execute(null);
        for (ushort species = 1; species <= save.MaxSpeciesID; species++)
        {
            Assert.False(save.Zukan.GetCaught(species));
            Assert.False(save.Zukan.GetSeen(species));
        }
        Assert.Equal(save.Zukan.Spinda, new Pokedex5EditorViewModel(save).SpindaPID is { } pid ? Convert.ToUInt32(pid, 16) : 0);
    }
    [Fact]
    public void CurrentSpeciesScopeAndNavigationRetainPendingFlagsWithoutTouchingOtherEntries()
    {
        var save = Load("gen5_white2.sav");
        var otherBefore = save.Zukan.GetCaught(2);
        var before = save.Zukan.Data.ToArray();
        var vm = new Pokedex5EditorViewModel(save) { WholeDex = false };
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 1);
        vm.Caught = !save.Zukan.GetCaught(1);
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 2);
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 1);
        Assert.Equal(!save.Zukan.GetCaught(1), vm.Caught);
        vm.CaughtAllCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        vm.SaveCommand.Execute(null);
        Assert.True(save.Zukan.GetCaught(1));
        Assert.Equal(otherBefore, save.Zukan.GetCaught(2));
    }
    [Theory]
    [InlineData("gen5_black.sav")]
    [InlineData("gen5_white2.sav")]
    public void LanguageAndFormOperationsHonorFormatAndScope(string file)
    {
        var save = Load(file);
        var vm = new Pokedex5EditorViewModel(save) { WholeDex = false, BulkLanguage = 1 };
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 201);
        vm.SetLanguagesCommand.Execute(null);
        vm.BulkRegularForms = false;
        vm.BulkShinyForms = true;
        vm.SetFormsCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.True(save.Zukan.GetLanguageFlag(201, 1));
        var (index, count) = save.Zukan.GetFormIndex(201);
        for (int i = 0; i < count; i++)
        {
            Assert.False(save.Zukan.GetFormFlag(index + i, 0));
            Assert.True(save.Zukan.GetFormFlag(index + i, 1));
            Assert.False(save.Zukan.GetFormFlag(index + i, 2));
            Assert.Equal(i == 0, save.Zukan.GetFormFlag(index + i, 3));
        }
        vm.ClearLanguagesCommand.Execute(null);
        vm.ClearFormsCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.False(save.Zukan.GetLanguageFlag(201, 1));
        for (int i = 0; i < count; i++) for (int region = 0; region < 4; region++) Assert.False(save.Zukan.GetFormFlag(index + i, region));
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 646);
        Assert.Equal(save is SAV5B2W2, vm.HasForms);
    }
    [Fact]
    public void InterleavedRegularAndShinyFormEditsRoundTripExactly()
    {
        var save = Load("gen5_white2.sav");
        var vm = new Pokedex5EditorViewModel(save) { WholeDex = false };
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 201);
        vm.ClearFormsCommand.Execute(null);
        vm.FormsSeen[0].IsChecked = true;
        vm.FormsSeen[3].IsChecked = true;
        vm.FormsSeen[10].IsChecked = true;
        vm.FormsDisplayed[3].IsChecked = true;
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 1);
        vm.SelectedSpecies = vm.Species.Single(s => s.Value == 201);
        Assert.True(vm.FormsSeen[0].IsChecked);
        Assert.True(vm.FormsSeen[3].IsChecked);
        Assert.True(vm.FormsSeen[10].IsChecked);
        vm.SaveCommand.Execute(null);
        var (index, _) = save.Zukan.GetFormIndex(201);
        Assert.True(save.Zukan.GetFormFlag(index, 0));
        Assert.True(save.Zukan.GetFormFlag(index + 1, 1));
        Assert.True(save.Zukan.GetFormFlag(index + 5, 0));
        Assert.True(save.Zukan.GetFormFlag(index + 1, 3));
        Assert.False(save.Zukan.GetFormFlag(index, 1));
    }
    [Fact]
    public void SeenMaskUsesValidGendersAndOneDisplayedChoice()
    {
        var save = Load("gen5_white2.sav");
        var vm = new Pokedex5EditorViewModel(save)
        {
            BulkMale = false, BulkFemale = false, BulkMaleShiny = true, BulkFemaleShiny = true,
        };
        vm.ApplySeenSelectionCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        for (ushort species = 1; species <= save.MaxSpeciesID; species++)
        {
            Assert.False(save.Zukan.GetSeen(species, 0));
            Assert.False(save.Zukan.GetSeen(species, 1));
            Assert.Equal(!save.Personal[species].OnlyFemale, save.Zukan.GetSeen(species, 2));
            Assert.Equal(!save.Personal[species].OnlyMale && !save.Personal[species].Genderless, save.Zukan.GetSeen(species, 3));
            Assert.InRange(Enumerable.Range(0, 4).Count(region => save.Zukan.GetDisplayed(species, region)), 0, 1);
        }
    }
    [Fact]
    public void CancelAndNoOpPreserveAllDexBytesAndInvalidPidBlocksSave()
    {
        var save = Load("gen5_white2.sav");
        var before = save.Zukan.Data.ToArray();
        save.State.Edited = false;
        var vm = new Pokedex5EditorViewModel(save);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        Assert.False(save.State.Edited);
        vm.NationalDexUnlocked = !vm.NationalDexUnlocked;
        vm.SpindaPID = "not hex";
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
        bool closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, save.Zukan.Data.ToArray());
    }
    [AvaloniaFact]
    public void CompactViewExposesScopeBulkActionsAndSaveCancel()
    {
        using var app = new HeadlessAppFixture();
        var vm = new Pokedex5EditorViewModel(Load("gen5_white2.sav"));
        app.Window.Width = 900;
        app.Window.Height = 600;
        var view = new Pokedex5Editor { DataContext = vm };
        app.Window.Content = view;
        app.Pump();
        var save = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveCommand));
        var cancel = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.CancelCommand));
        Assert.True(save.IsEffectivelyVisible);
        Assert.True(cancel.IsEffectivelyVisible);
        var caught = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.CaughtAllCommand));
        Assert.True(caught.Bounds.Height > 0);
        var expander = view.GetVisualDescendants().OfType<Expander>().Single();
        expander.IsExpanded = true;
        app.Pump();
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => ReferenceEquals(b.Command, vm.SetLanguagesCommand));
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => ReferenceEquals(b.Command, vm.SetFormsCommand));
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
        {
            var dir = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!;
            Directory.CreateDirectory(dir);
            Assert.NotNull(app.CaptureFrame(Path.Combine(dir, "pokedex5-bulk.png")));
        }
    }
    [Fact]
    public void NoOpPreservesUnusualPackedBitsAndNationalModeExactly()
    {
        var save = Load("gen5_white2.sav");
        save.Zukan.Packed = 0xFFC00800;
        save.Zukan.Spinda = 0xDEADBEEF;
        var before = save.Zukan.Data.ToArray();
        var vm = new Pokedex5EditorViewModel(save);
        vm.SaveCommand.Execute(null);
        Assert.Equal(before, save.Zukan.Data.ToArray());
    }

    [AvaloniaFact]
    public void LongLocalizedLabelsAndSaveActionsFitMinimumShellViewport()
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture();
        try
        {
            app.ViewModel.LanguageService.SetLanguage("de");
            var vm = new Pokedex5EditorViewModel(Load("gen5_white2.sav"));
            app.Window.Width = 1024;
            app.Window.Height = 720;
            var view = new Pokedex5Editor { DataContext = vm };
            app.Window.Content = view;
            app.Pump();
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.Command, vm.SaveCommand)
                || ReferenceEquals(b.Command, vm.CancelCommand) || ReferenceEquals(b.Command, vm.CaughtAllCommand)))
            {
                var point = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), app.Window)!.Value;
                Assert.InRange(point.X, 1, 1024);
                Assert.InRange(point.Y, 1, 720);
            }
            if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") == "1")
                Assert.NotNull(app.CaptureFrame(Path.Combine(Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")!, "pokedex5-de-minimum.png")));
        }
        finally { app.ViewModel.LanguageService.SetLanguage(previous); }
    }

}
