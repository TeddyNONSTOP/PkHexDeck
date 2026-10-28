using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using Xunit;

namespace PKHeX.Avalonia.Tests;

public class StatExperienceTests
{
    [AvaloniaTheory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    public void RealizedEditorPreservesAndEditsFullStatExperience(GameVersion version)
    {
        var save = BlankSaveFile.Get(version);
        var pokemon = Assert.IsAssignableFrom<GBPKM>(SaveFileFactory.CreateTestPKM(save));
        pokemon.EV_HP = 40000;
        pokemon.EV_ATK = 256;
        pokemon.EV_DEF = 32768;
        pokemon.EV_SPC = 60000;
        pokemon.EV_SPE = 65535;
        var (vm, _, _) = TestHelpers.CreateTestViewModel(pokemon, save);
        var view = new PokemonEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 360, Height = 700 };
        using var lifetime = new HeadlessWindowLifetime(window);
        vm.SelectEditorSectionCommand.Execute("1");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var names = new[] { vm.HpEvAutomationName, vm.AtkEvAutomationName, vm.DefEvAutomationName,
            vm.SpaEvAutomationName, vm.SpdEvAutomationName, vm.SpeEvAutomationName };
        var inputs = names.Select(name => view.GetVisualDescendants().OfType<NumericUpDown>()
            .Single(input => AutomationProperties.GetName(input) == name)).ToArray();
        var expected = new[] { 40000, 256, 32768, 60000, 60000, 65535 };
        for (var i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(65535m, inputs[i].Maximum);
            Assert.Equal((decimal)expected[i], inputs[i].Value);
            var prepared = vm.PreparePKM();
            var values = new[] { prepared.EV_HP, prepared.EV_ATK, prepared.EV_DEF,
                prepared.EV_SPA, prepared.EV_SPD, prepared.EV_SPE };
            Assert.Equal(expected[i], values[i]);
        }

        inputs[0].Value = 65535;
        inputs[3].Value = 50000;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(65535, vm.EvHP);
        Assert.Equal(50000, vm.EvSPA);
        Assert.Equal(50000, vm.EvSPD);
        Assert.Equal(50000m, inputs[4].Value);
        inputs[4].Value = 54321;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(54321, vm.EvSPA);
        Assert.Equal(54321m, inputs[3].Value);

        save.SetBoxSlotAtIndex(vm.PreparePKM(), 0);
        var reloaded = save.GetBoxSlotAtIndex(0);
        Assert.Equal(65535, reloaded.EV_HP);
        Assert.Equal(54321, reloaded.EV_SPA);
        Assert.Equal(54321, reloaded.EV_SPD);
        Assert.Equal(65535 + 256 + 32768 + 54321 + 65535, vm.EVTotal);
        Assert.Equal(expected[0], pokemon.EV_HP);
        Assert.Equal(expected[3], pokemon.EV_SPC);

        vm.ClearEVsCommand.Execute(null);
        Assert.Equal(0, vm.EVTotal);
        Assert.Equal(0, vm.PreparePKM().EV_SPA);
        Assert.Equal(0, vm.EvSPD);
    }

    [AvaloniaTheory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    public void ReusedViewUpdatesLimitsBeforeLoadingAnotherGeneration(GameVersion version)
    {
        var modernSave = BlankSaveFile.Get(GameVersion.X);
        var modernPokemon = SaveFileFactory.CreateTestPKM(modernSave);
        modernPokemon.EV_HP = 252;
        var (modern, _, _) = TestHelpers.CreateTestViewModel(modernPokemon, modernSave);
        var olderSave = BlankSaveFile.Get(version);
        var olderPokemon = SaveFileFactory.CreateTestPKM(olderSave);
        olderPokemon.EV_HP = 65535;
        var (older, _, _) = TestHelpers.CreateTestViewModel(olderPokemon, olderSave);
        modern.SelectEditorSectionCommand.Execute("1");
        older.SelectEditorSectionCommand.Execute("1");
        var view = new PokemonEditor { DataContext = modern };
        var window = new Window { Content = view, Width = 360, Height = 700 };
        using var lifetime = new HeadlessWindowLifetime(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.DataContext = older;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(65535, older.EvHP);
        Assert.Equal(65535, older.PreparePKM().EV_HP);
        var next = olderPokemon.Clone();
        next.EV_HP = 54321;
        Assert.True(older.LoadPKM(next));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(54321, older.EvHP);
        view.DataContext = modern;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(252, modern.EvHP);
        Assert.Equal(252, modern.PreparePKM().EV_HP);
    }

    [AvaloniaTheory]
    [InlineData(GameVersion.E, 255)]
    [InlineData(GameVersion.B, 255)]
    [InlineData(GameVersion.X, 252)]
    public void RealizedEditorUsesFormatLimitAndKeepsModernSpecialStatsIndependent(GameVersion version, int maximum)
    {
        var save = BlankSaveFile.Get(version);
        var pokemon = SaveFileFactory.CreateTestPKM(save);
        pokemon.EV_HP = maximum;
        var (vm, _, _) = TestHelpers.CreateTestViewModel(pokemon, save);
        var view = new PokemonEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 360, Height = 700 };
        using var lifetime = new HeadlessWindowLifetime(window);
        vm.SelectEditorSectionCommand.Execute("1");
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var input = view.GetVisualDescendants().OfType<NumericUpDown>()
            .Single(control => AutomationProperties.GetName(control) == vm.HpEvAutomationName);
        Assert.Equal((decimal)maximum, input.Maximum);
        Assert.Equal((decimal)maximum, input.Value);
        Assert.Equal(maximum, vm.PreparePKM().EV_HP);
        vm.EvSPA = 100;
        vm.EvSPD = 150;
        Assert.Equal(100, vm.PreparePKM().EV_SPA);
        Assert.Equal(150, vm.PreparePKM().EV_SPD);
    }
}
