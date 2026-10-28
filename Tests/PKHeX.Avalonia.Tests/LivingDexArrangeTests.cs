using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PKHeX.Application.UseCases;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Living Dex autopilot: the Pokémon already in the boxes are moved into national dex order, nothing is
/// created or lost, and the party and earlier boxes stay as they are.
/// </summary>
public sealed class LivingDexArrangeTests
{
    private const ushort Bulbasaur = 1, Pikachu = 25, Eevee = 133, Pidgey = 16;

    private static PKM Make(SaveFile sav, ushort species, bool shiny = false, bool egg = false, string nickname = "")
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.CurrentLevel = 5;
        // A blank entity has PID 0 and trainer ID 0, which reads as shiny; make the default plain.
        if (shiny) pk.SetShiny(); else pk.SetUnshiny();
        if (egg) pk.IsEgg = true;
        if (nickname.Length != 0) { pk.IsNicknamed = true; pk.Nickname = nickname; }
        pk.RefreshChecksum();
        return pk;
    }

    /// <summary>Box 1: Pikachu, Bulbasaur, a second Pikachu (shiny), an Eevee egg, a Pidgey (not in this game).</summary>
    private static SaveFile CreateSave()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);
        sav.SetBoxSlotAtIndex(Make(sav, Pikachu, nickname: "Plain"), 0, 0);
        sav.SetBoxSlotAtIndex(Make(sav, Bulbasaur), 0, 5);
        sav.SetBoxSlotAtIndex(Make(sav, Pikachu, shiny: true, nickname: "Shiny"), 0, 9);
        sav.SetBoxSlotAtIndex(Make(sav, Eevee, egg: true), 0, 12);
        sav.SetBoxSlotAtIndex(Make(sav, Pidgey), 0, 20);
        sav.SetPartySlotAtIndex(Make(sav, Eevee), 0);
        return sav;
    }

    private static (int Box, int Slot) PositionOf(SaveFile sav, ushort species, bool includeForms = false)
    {
        var index = LivingDexTrackerUseCase.GetTargets(sav, includeForms).ToList().IndexOf((species, 0));
        return (index / sav.BoxSlotCount, index % sav.BoxSlotCount);
    }

    private static List<string> BoxContents(SaveFile sav, int fromBox = 0)
    {
        var all = new List<string>();
        for (var box = fromBox; box < sav.BoxCount; box++)
            for (var slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species != 0)
                    all.Add(Convert.ToBase64String(pk.Data));
            }
        all.Sort(StringComparer.Ordinal);
        return all;
    }

    private static PKM At(SaveFile sav, (int Box, int Slot) p) => sav.GetBoxSlotAtIndex(p.Box, p.Slot);

    [Fact]
    public void Arrange_WithGaps_PutsEachSpeciesAtItsDexSlot_AndKeepsEveryPokemon()
    {
        var sav = CreateSave();
        Assert.False(PidgeyIsInGame(sav)); // the scenario needs a species outside this game
        var before = BoxContents(sav);
        var party = sav.GetPartySlotAtIndex(0).Data.ToArray();
        var useCase = new LivingDexArrangeUseCase();

        var plan = useCase.Plan(sav, new LivingDexArrangeOptions(0, false, LeaveGaps: true, PreferShiny: true));
        Assert.True(plan.Fits);
        Assert.Equal(2, plan.Placed);
        useCase.Execute(sav, plan);

        Assert.Equal(Bulbasaur, At(sav, (0, 0)).Species);
        var pikachu = At(sav, PositionOf(sav, Pikachu));
        Assert.Equal(Pikachu, pikachu.Species);
        Assert.True(pikachu.IsShiny); // Prefer shiny chose the shiny one for the dex slot
        Assert.Equal(before, BoxContents(sav)); // nothing created, lost or changed
        Assert.Equal(party, sav.GetPartySlotAtIndex(0).Data.ToArray());
        Assert.Equal(3, plan.Extras); // plain Pikachu, the egg and Pidgey go after the dex
        Assert.Equal(1, plan.PartyOnly); // Eevee is only in the party

        // A second run finds nothing to do.
        Assert.Empty(useCase.Plan(sav, new LivingDexArrangeOptions(0, false, true, true)).Writes);
    }

    private static bool PidgeyIsInGame(SaveFile sav) => sav.Personal.IsSpeciesInGame(Pidgey);

    [Fact]
    public void Arrange_WithoutGaps_PacksTheDexAndKeepsOriginalOrderForTheRest()
    {
        var sav = CreateSave();
        var useCase = new LivingDexArrangeUseCase();

        var plan = useCase.Plan(sav, new LivingDexArrangeOptions(0, false, LeaveGaps: false, PreferShiny: false));
        useCase.Execute(sav, plan);

        Assert.Equal(Bulbasaur, At(sav, (0, 0)).Species);
        Assert.Equal("Plain", At(sav, (0, 1)).Nickname); // earliest Pikachu when shiny is not preferred
        Assert.Equal("Shiny", At(sav, (0, 2)).Nickname); // the duplicate follows the dex
        Assert.True(At(sav, (0, 3)).IsEgg);
        Assert.Equal(0, At(sav, (0, 5)).Species);
    }

    [Fact]
    public void Arrange_LeavesEarlierBoxesAlone()
    {
        var sav = CreateSave();
        sav.SetBoxSlotAtIndex(Make(sav, Eevee), 2, 0);
        var firstBoxes = Enumerable.Range(0, 2).SelectMany(b => Enumerable.Range(0, sav.BoxSlotCount)
            .Select(s => sav.GetBoxSlotAtIndex(b, s).Data.ToArray())).ToList();
        var useCase = new LivingDexArrangeUseCase();

        var plan = useCase.Plan(sav, new LivingDexArrangeOptions(StartBox: 2, false, LeaveGaps: false, PreferShiny: true));
        useCase.Execute(sav, plan);

        Assert.Equal(firstBoxes, Enumerable.Range(0, 2).SelectMany(b => Enumerable.Range(0, sav.BoxSlotCount)
            .Select(s => sav.GetBoxSlotAtIndex(b, s).Data.ToArray())).ToList());
        Assert.Equal(Eevee, At(sav, (2, 0)).Species);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void Arrange_ThatDoesNotFit_IsRefusedWithoutChanges()
    {
        var sav = CreateSave();
        var useCase = new LivingDexArrangeUseCase();
        var lastBox = sav.BoxCount - 1;
        sav.SetBoxSlotAtIndex(Make(sav, (ushort)sav.Personal.MaxSpeciesID), lastBox, 0);
        var before = BoxContents(sav);

        // From the last box alone, gaps up to the final species need far more than one box.
        var plan = useCase.Plan(sav, new LivingDexArrangeOptions(lastBox, false, LeaveGaps: true, PreferShiny: true));

        Assert.False(plan.Fits);
        Assert.True(plan.ShortBy > 0);
        Assert.Empty(plan.Writes);
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(sav, plan));
        Assert.Equal(before, BoxContents(sav));
    }

    [AvaloniaFact]
    public async Task Autopilot_AsksFirst_AppliesAsOneUndoStep()
    {
        using var app = new HeadlessAppFixture();
        var sav = CreateSave();
        app.LoadSaveInstance(sav);
        var original = Enumerable.Range(0, sav.BoxSlotCount).Select(s => sav.GetBoxSlotAtIndex(0, s).Data.ToArray()).ToList();

        app.ViewModel.OpenLivingDexArrangeCommand.Execute(null);
        var vm = Assert.IsType<LivingDexArrangeViewModel>(app.Windows.FocusedTool);
        Assert.NotEmpty(vm.Moves);
        Assert.True(vm.ArrangeCommand.CanExecute(null));

        app.Dialogs.ConfirmResult = false;
        await vm.ArrangeCommand.ExecuteAsync(null);
        Assert.Equal(Pikachu, sav.GetBoxSlotAtIndex(0, 0).Species);

        app.Dialogs.ConfirmResult = true;
        await vm.ArrangeCommand.ExecuteAsync(null);
        app.Pump();
        Assert.Equal(Bulbasaur, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.True(sav.State.Edited);
        Assert.Empty(vm.Moves); // the preview now shows nothing left to do

        app.ViewModel.UndoCommand.Execute(null);
        app.Pump();
        Assert.Equal(original, Enumerable.Range(0, sav.BoxSlotCount).Select(s => sav.GetBoxSlotAtIndex(0, s).Data.ToArray()).ToList());
    }

    [AvaloniaFact]
    public void Tracker_ArrangeButton_OpensTheAutopilot()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(CreateSave());
        app.ViewModel.OpenLivingDexTrackerCommand.Execute(null);
        var tracker = Assert.IsType<LivingDexTrackerViewModel>(app.Windows.FocusedTool);

        tracker.ArrangeCommand.Execute(null);

        Assert.IsType<LivingDexArrangeViewModel>(app.Windows.FocusedTool);
    }
}
