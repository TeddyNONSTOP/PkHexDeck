using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Moq;
using PKHeX.Application.UseCases;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Living Dex tracker: counts what the save (and optionally the Bank) holds against every species or
/// form obtainable in the save's game, without changing the save.
/// </summary>
public sealed class LivingDexTrackerTests
{
    private const ushort Pikachu = 25, Eevee = 133, Rotom = 479, Mimikyu = 778, Sprigatito = 906;

    private static PKM Make(SaveFile sav, ushort species, byte form = 0, bool shiny = false, bool egg = false)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.Form = form;
        pk.CurrentLevel = 5;
        // A blank entity has PID 0 and trainer ID 0, which reads as shiny; make the default plain.
        if (shiny)
            pk.SetShiny();
        else
            pk.SetUnshiny();
        if (egg)
            pk.IsEgg = true;
        pk.RefreshChecksum();
        return pk;
    }

    private static SaveFile CreateSave()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        sav.SetBoxSlotAtIndex(Make(sav, Pikachu), 0, 0);
        sav.SetBoxSlotAtIndex(Make(sav, Pikachu, shiny: true), 1, 3);
        sav.SetBoxSlotAtIndex(Make(sav, Rotom, form: 2), 0, 5);
        sav.SetBoxSlotAtIndex(Make(sav, Sprigatito, form: 3), 0, 6); // a form Sprigatito does not have
        sav.SetPartySlotAtIndex(Make(sav, Eevee, egg: true), 0);
        return sav;
    }

    [Fact]
    public void Targets_FollowTheGame_AndSkipBattleOnlyForms()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        var species = LivingDexTrackerUseCase.GetTargets(sav, includeForms: false);
        var forms = LivingDexTrackerUseCase.GetTargets(sav, includeForms: true);

        Assert.Equal(Enumerable.Range(1, sav.Personal.MaxSpeciesID).Count(s => sav.Personal.IsSpeciesInGame((ushort)s)), species.Count);
        Assert.All(species, t => Assert.Equal(0, t.Form));
        Assert.True(forms.Count > species.Count);
        Assert.Contains((Rotom, (byte)2), forms);
        Assert.DoesNotContain((Mimikyu, (byte)1), forms); // Busted Form only appears in battle
        Assert.Equal(species.Select(t => t.Species), forms.Select(t => t.Species).Distinct());
    }

    [Fact]
    public void Build_CountsOwnedAndShiny_AndLeavesEggsOut()
    {
        var sav = CreateSave();
        var report = new LivingDexTrackerUseCase().Build(sav, includeForms: false, LivingDexTrackerUseCase.ReadSave(sav));

        var pikachu = report.Entries.Single(e => e.Species == Pikachu);
        Assert.Equal(2, pikachu.Count);
        Assert.Equal(1, pikachu.ShinyCount);
        Assert.False(report.Entries.Single(e => e.Species == Eevee).IsOwned);
        Assert.Equal(1, report.EggCount);
        Assert.Equal(3, report.OwnedCount); // Pikachu, Rotom, Sprigatito
        Assert.Equal(1, report.ShinyCount);
    }

    [Fact]
    public void Build_FormMode_CountsEachForm_AndFallsBackToTheBaseForm()
    {
        var sav = CreateSave();
        var report = new LivingDexTrackerUseCase().Build(sav, includeForms: true, LivingDexTrackerUseCase.ReadSave(sav));

        Assert.True(report.Entries.Single(e => e.Species == Rotom && e.Form == 2).IsOwned);
        Assert.False(report.Entries.Single(e => e.Species == Rotom && e.Form == 0).IsOwned);
        Assert.True(report.Entries.Single(e => e.Species == Sprigatito && e.Form == 0).IsOwned);
    }

    [Fact]
    public void Build_NeverChangesTheSave()
    {
        var sav = CreateSave();
        var before = sav.Write().ToArray();
        sav.State.Edited = false;

        new LivingDexTrackerUseCase().Build(sav, includeForms: true, LivingDexTrackerUseCase.ReadSave(sav));

        Assert.Equal(before, sav.Write().ToArray());
        Assert.False(sav.State.Edited);
    }

    [Fact]
    public void ReadBank_SkipsEntriesBeingWithdrawnIntoASave()
    {
        var bank = new Mock<IBankService>();
        BankEntry Entry(ushort species, int slot, BankPendingWithdrawal? pending = null) => new(
            Guid.NewGuid(), 0, slot, new BankEntryInfo(species, 0, false, "", 5, 9, ".pk9", 0, false, ""),
            DateTimeOffset.UtcNow, pending);
        var withdrawing = new BankPendingWithdrawal(Guid.NewGuid(), "save", null, 0, 0, DateTimeOffset.UtcNow);
        bank.Setup(b => b.GetAll()).Returns([Entry(Eevee, 0), Entry(Pikachu, 1, withdrawing)]);

        var holdings = LivingDexTrackerUseCase.ReadBank(bank.Object).ToList();

        var only = Assert.Single(holdings);
        Assert.Equal(Eevee, only.Species);
        Assert.Equal(LivingDexSourceKind.Bank, only.Source);
    }

    private static LivingDexTrackerViewModel CreateViewModel(SaveFile sav, IBankService? bank = null)
        => new(sav, new Mock<ISpriteRenderer>().Object, bank);

    [Fact]
    public void ViewModel_FiltersSearchesAndSummarises()
    {
        var sav = CreateSave();
        var vm = CreateViewModel(sav);
        var total = vm.Rows.Count;

        Assert.Contains("3", vm.SummaryText);
        Assert.Contains("1", vm.EggText);
        Assert.Equal("Box 2, slot 4", vm.Rows.Single(r => r.Species == Pikachu).Locations.Split("; ")[1]);

        vm.FilterIndex = (int)LivingDexTrackerFilter.Missing;
        Assert.Equal(total - 3, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, r => r.Species == Pikachu);

        vm.FilterIndex = (int)LivingDexTrackerFilter.Owned;
        Assert.Equal(3, vm.Rows.Count);

        vm.FilterIndex = (int)LivingDexTrackerFilter.NoShiny;
        Assert.DoesNotContain(vm.Rows, r => r.Species == Pikachu);

        vm.FilterIndex = (int)LivingDexTrackerFilter.All;
        vm.SearchText = "25";
        Assert.Equal(Pikachu, Assert.Single(vm.Rows).Species);
        vm.SearchText = "pika";
        Assert.Equal(Pikachu, Assert.Single(vm.Rows).Species);
    }

    [Fact]
    public void ViewModel_IncludeBank_AddsBankPokemonThatCannotBeShownInABox()
    {
        var sav = CreateSave();
        var bank = new Mock<IBankService>();
        bank.Setup(b => b.GetAll()).Returns([new BankEntry(Guid.NewGuid(), 2, 7,
            new BankEntryInfo(Eevee, 0, true, "", 5, 9, ".pk9", 0, false, ""), DateTimeOffset.UtcNow)]);
        var vm = CreateViewModel(sav, bank.Object);
        Assert.True(vm.CanIncludeBank);
        Assert.False(vm.Rows.Single(r => r.Species == Eevee).IsOwned);

        vm.IncludeBank = true;

        var eevee = vm.Rows.Single(r => r.Species == Eevee);
        Assert.True(eevee.HasShiny);
        Assert.Equal("Bank box 3, slot 8", eevee.Locations);
        vm.SelectedRow = eevee;
        Assert.False(vm.ShowSelectedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void MainWindow_OpensTheTracker_AndShowsThePokemonInItsBox()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(CreateSave());

        app.ViewModel.OpenLivingDexTrackerCommand.Execute(null);
        var vm = Assert.IsType<LivingDexTrackerViewModel>(app.Windows.FocusedTool);

        vm.SelectedRow = vm.Rows.Single(r => r.Species == Rotom);
        vm.ShowSelectedCommand.Execute(null);
        app.Pump();

        var navigator = (IBoxNavigator)app.BoxViewer!;
        Assert.Equal(0, navigator.CurrentBox);
        Assert.Equal(5, navigator.CurrentSlot);
        Assert.Equal(Rotom, app.ViewModel.CurrentPokemonEditor!.Species);

        // Reopening focuses the same window instead of opening another.
        app.ViewModel.OpenLivingDexTrackerCommand.Execute(null);
        Assert.Single(app.Windows.ShownTools, t => t.ViewModel is LivingDexTrackerViewModel);
    }
}
