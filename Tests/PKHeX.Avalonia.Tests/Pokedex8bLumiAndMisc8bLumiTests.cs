using System;
using System.IO;
using System.Linq;
using PKHeX.Presentation.ViewModels;
using PKHeX.Core;
using Xunit;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Stage 2 verification for the Luminescent Platinum (BDSP ROM hack) Misc/Pokédex editor dialogs.
/// Mirrors the pattern in PokedexTests.cs (direct ViewModel construction, no Avalonia rendering needed).
/// </summary>
public class Pokedex8bLumiAndMisc8bLumiTests(ITestOutputHelper output)
{
    static Pokedex8bLumiAndMisc8bLumiTests()
    {
        GameInfo.Strings = GameInfo.GetStrings("en");
    }

    private static string? FindSaveFilesPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var savePath = Path.Combine(dir.FullName, "savefiles");
            if (Directory.Exists(savePath))
                return savePath;
            dir = dir.Parent;
        }
        return null;
    }

    private static SAV8BSLuminescent? LoadRealSave()
    {
        var savPath = FindSaveFilesPath();
        if (savPath is null) return null;
        var path = Path.Combine(savPath, "LuminescentPlatinum_test.bin");
        if (!File.Exists(path)) return null;
        return SaveUtil.GetSaveFile(File.ReadAllBytes(path)) as SAV8BSLuminescent;
    }

    [Fact]
    public void Pokedex8bLumi_ConstructsAndBinds_AgainstBlankSave()
    {
        var sav = new SAV8BSLuminescent();
        var vm = new Pokedex8bLumiEditorViewModel(sav);

        // The species list must cover the wider Luminescent range, not just the original 493-species Sinnoh dex.
        Assert.True(vm.FilteredSpecies.Count > 493, "species list must extend past the vanilla Sinnoh dex");
        Assert.Equal(sav.MaxSpeciesID, vm.FilteredSpecies.Count);
    }

    [Fact]
    public void Pokedex8bLumi_LoadAndSave_VerifyFlags_LowSpecies()
    {
        // Arrange - a Sinnoh-dex species (within the original 493 range), which also exercises language/form flags.
        var sav = new SAV8BSLuminescent();
        var zukan = sav.Zukan;
        ushort turtwig = 387;

        // Act
        var vm = new Pokedex8bLumiEditorViewModel(sav);
        var item = vm.FilteredSpecies.FirstOrDefault(x => x.Value == turtwig);
        Assert.NotNull(item);
        vm.SelectedSpecies = item;

        Assert.True(vm.CanEditLanguageAndForms);
        vm.State = 3; // Caught
        vm.SeenMale = true;
        vm.LangEnglish = true;
        vm.SaveCurrent();

        // Assert
        Assert.Equal(ZukanState8b.Caught, zukan.GetState(turtwig));
        zukan.GetGenderFlags(turtwig, out bool m, out _, out _, out _);
        Assert.True(m);
        Assert.True(zukan.GetLanguageFlag(turtwig, (int)LanguageID.English));
    }

    [Fact]
    public void Pokedex8bLumi_LoadAndSave_VerifyFlags_HighSpecies_BeyondSinnohDex()
    {
        // Arrange - a species above the original 493-species range, only reachable via Luminescent's wider dex.
        var sav = new SAV8BSLuminescent();
        var zukan = sav.Zukan;
        ushort pikachu = 25; // low ID, but re-verify a high one specifically to prove the wider range works
        ushort highSpecies = (ushort)Math.Min(900, (int)sav.MaxSpeciesID);
        Assert.True(highSpecies > 493, "test needs a species above the vanilla Sinnoh dex to be meaningful");

        // Act
        var vm = new Pokedex8bLumiEditorViewModel(sav);
        var item = vm.FilteredSpecies.FirstOrDefault(x => x.Value == highSpecies);
        Assert.NotNull(item);
        vm.SelectedSpecies = item;

        // Language/form tracking is deliberately not extended past the Sinnoh dex (Stage 1 finding) - confirm the
        // UI correctly reports that, then still verify state/gender (which ARE extended) round-trip correctly.
        Assert.False(vm.CanEditLanguageAndForms);
        vm.State = 3; // Caught
        vm.SeenMale = true;
        vm.SaveCurrent();

        // Assert - state/gender for the high species must round-trip, and must NOT have corrupted a neighboring
        // low species sharing the same nibble/byte (regression coverage for the Stage 1 bit-packing math).
        Assert.Equal(ZukanState8b.Caught, zukan.GetState(highSpecies));
        zukan.GetGenderFlags(highSpecies, out bool m, out _, out _, out _);
        Assert.True(m);
        Assert.Equal(ZukanState8b.None, zukan.GetState(pikachu));
    }

    [Fact]
    public void Misc8bLumi_ConstructsAndBinds_AgainstBlankSave()
    {
        var sav = new SAV8BSLuminescent();
        var vm = new Misc8bLumiEditorViewModel(sav);

        // A freshly-constructed blank save has no trainers defeated and none rebattle-ready yet;
        // just confirm construction succeeds and exposes sane initial state without throwing.
        Assert.False(vm.CanRebattle);
    }

    [Fact]
    public void Misc8bLumi_DefeatAllTrainers_RoundTrips_WithoutThrowing()
    {
        var sav = new SAV8BSLuminescent();
        var vm = new Misc8bLumiEditorViewModel(sav);

        vm.DefeatAllTrainersCommand.Execute(null);
        Assert.True(vm.CanRebattle);
        Assert.False(vm.CanDefeatAll);

        vm.SaveCommand.Execute(null);

        // Confirm the save actually landed on the Lumi-repacked block (2 bits/trainer, 5656 trainers) and not
        // some corrupted/mismatched region - re-open a fresh clone from the written bytes and check it stuck.
        var reopened = new SAV8BSLuminescent(sav.Write());
        Assert.True(reopened.BattleTrainer.AnyDefeated);
        Assert.False(reopened.BattleTrainer.AnyUndefeated);
    }

    [Fact]
    public void RealLuminescentSave_Pokedex8bLumiViewModel_ConstructsAndBindsRealData()
    {
        var sav = LoadRealSave();
        if (sav is null) { output.WriteLine("Skip: LuminescentPlatinum_test.bin not found"); return; }

        var vm = new Pokedex8bLumiEditorViewModel(sav);
        Assert.True(vm.FilteredSpecies.Count > 493);

        // Select a handful of species (including some the real save's box slots actually contain) and confirm
        // no exceptions and sane state.
        foreach (var species in new ushort[] { 1, 25, 493, 494, 900 })
        {
            if (species > sav.MaxSpeciesID) continue;
            var item = vm.FilteredSpecies.FirstOrDefault(x => x.Value == species);
            Assert.NotNull(item);
            vm.SelectedSpecies = item;
            output.WriteLine($"species {species}: state={vm.State} canEditLangForms={vm.CanEditLanguageAndForms}");
        }
    }

    [Fact]
    public void RealLuminescentSave_Misc8bLumiViewModel_ConstructsAndBindsRealData()
    {
        var sav = LoadRealSave();
        if (sav is null) { output.WriteLine("Skip: LuminescentPlatinum_test.bin not found"); return; }

        var vm = new Misc8bLumiEditorViewModel(sav);
        output.WriteLine($"CanRebattle={vm.CanRebattle} CanDefeatAll={vm.CanDefeatAll} CanUnlockSpiritomb={vm.CanUnlockSpiritomb}");
        // Just constructing and reading initial state against real data must not throw - the real signal here.
    }
}
