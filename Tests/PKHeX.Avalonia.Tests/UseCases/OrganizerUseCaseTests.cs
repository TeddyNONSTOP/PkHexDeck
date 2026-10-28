using PKHeX.Application.UseCases;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests.UseCases;

/// <summary>
/// Framework-free coverage for the Organizer's Application layer: the mark bookkeeping
/// (<see cref="BoxSelectionService"/>) and the three bulk use cases' planning/execution policies
/// (fill-forward into empty slots only, skip-and-report, release refusal on protected slots, party
/// exclusion from release, never-overwrite export).
/// </summary>
public class OrganizerUseCaseTests
{
    private static SAV6XY NewSave() => new();

    private static void Put(SaveFile sav, int box, int slot, ushort species) =>
        sav.SetBoxSlotAtIndex(new PK6 { Species = species }, box, slot);

    private static ushort At(SaveFile sav, int box, int slot) => sav.GetBoxSlotAtIndex(box, slot).Species;

    /// <summary>A Sun/Moon save whose battle team 1 occupies box 0 slots 0..1; team 1 is locked when requested.</summary>
    private static SAV7SM NewSaveWithBattleTeam(bool locked)
    {
        var sav = new SAV7SM();
        sav.BoxLayout.ClearBattleTeams();
        sav.BoxLayout.TeamSlots[0] = 0;
        sav.BoxLayout.TeamSlots[1] = 1;
        sav.BoxLayout.SetIsTeamLocked(0, locked);
        return sav;
    }

    // ---------------------------------------------------------------- BoxSelectionService

    [Fact]
    public void Selection_ToggleMarksAndUnmarks_AndRaisesChanged()
    {
        var selection = new BoxSelectionService();
        var raised = 0;
        selection.SelectionChanged += (_, _) => raised++;

        selection.Toggle(SlotLocation.FromBox(0, 3));
        Assert.True(selection.IsMarked(SlotLocation.FromBox(0, 3)));
        Assert.Equal(1, selection.Count);

        selection.Toggle(SlotLocation.FromBox(0, 3));
        Assert.False(selection.IsMarked(SlotLocation.FromBox(0, 3)));
        Assert.Equal(0, selection.Count);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Selection_BoxAndPartyLocationsAreDistinct()
    {
        var selection = new BoxSelectionService();
        selection.Toggle(SlotLocation.FromParty(0));

        Assert.True(selection.IsMarked(SlotLocation.FromParty(0)));
        Assert.False(selection.IsMarked(SlotLocation.FromBox(0, 0)));
    }

    [Fact]
    public void Selection_MarkAllInBox_MarksOnlyOccupiedBoxSlots_NeverParty()
    {
        var sav = NewSave();
        Put(sav, 2, 0, 1);
        Put(sav, 2, 7, 4);
        sav.SetPartySlotAtIndex(new PK6 { Species = 25 }, 0);
        var selection = new BoxSelectionService();

        selection.MarkAllInBox(sav, 2);

        Assert.Equal(2, selection.Count);
        Assert.True(selection.IsMarked(SlotLocation.FromBox(2, 0)));
        Assert.True(selection.IsMarked(SlotLocation.FromBox(2, 7)));
        Assert.DoesNotContain(selection.MarkedSlots, location => location.IsParty);
    }

    [Fact]
    public void Selection_ExitSelectionMode_DropsAllMarks()
    {
        var selection = new BoxSelectionService();
        selection.EnterSelectionMode();
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromParty(1));

        selection.ExitSelectionMode();

        Assert.False(selection.IsSelectionModeEnabled);
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Selection_Prune_DropsMarksOnEmptySlots()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        var selection = new BoxSelectionService();
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 1)); // empty
        selection.Toggle(SlotLocation.FromParty(0)); // empty party

        selection.Prune(sav);

        Assert.Equal([SlotLocation.FromBox(0, 0)], selection.Snapshot());
    }

    [Fact]
    public void Selection_Snapshot_IsDeterministic_PartyFirstThenBoxOrder()
    {
        var selection = new BoxSelectionService();
        selection.Toggle(SlotLocation.FromBox(1, 0));
        selection.Toggle(SlotLocation.FromBox(0, 5));
        selection.Toggle(SlotLocation.FromParty(2));
        selection.Toggle(SlotLocation.FromBox(0, 1));

        Assert.Equal(
            [SlotLocation.FromParty(2), SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 5), SlotLocation.FromBox(1, 0)],
            selection.Snapshot());
    }

    // ---------------------------------------------------------------- BulkSlotTransferUseCase

    [Fact]
    public void Transfer_Plan_FillsForwardIntoEmptySlotsOnly()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        Put(sav, 0, 1, 4);
        Put(sav, 1, 0, 7); // occupied destination slot must be skipped, not overwritten

        var plan = new BulkSlotTransferUseCase().Plan(
            sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1)], SlotLocation.FromBox(1, 0), clone: false);

        Assert.Equal(
            [new BulkTransferEntry(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(1, 1)),
             new BulkTransferEntry(SlotLocation.FromBox(0, 1), SlotLocation.FromBox(1, 2))],
            plan.Entries);
        Assert.Equal(0, plan.SkippedNoRoom);
        Assert.Equal(0, plan.SkippedUnavailable);
    }

    [Fact]
    public void Transfer_FullDestination_ReportsSkippedNoRoom()
    {
        var sav = NewSave();
        for (var slot = 0; slot < sav.BoxSlotCount - 1; slot++)
            Put(sav, 1, slot, 10);
        Put(sav, 0, 0, 1);
        Put(sav, 0, 1, 4);
        Put(sav, 0, 2, 7);

        var plan = new BulkSlotTransferUseCase().Plan(
            sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 2)], SlotLocation.FromBox(1, 0), clone: false);

        Assert.Single(plan.Entries);
        Assert.Equal(2, plan.SkippedNoRoom);
        Assert.Equal(3, plan.RequestedCount);
    }

    [Fact]
    public void Transfer_Move_SkipsEmptyAndAlreadyInDestinationSources()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        Put(sav, 1, 5, 4);

        var plan = new BulkSlotTransferUseCase().Plan(
            sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 9), SlotLocation.FromBox(1, 5)], SlotLocation.FromBox(1, 0), clone: false);

        Assert.Equal([new BulkTransferEntry(SlotLocation.FromBox(0, 0), SlotLocation.FromBox(1, 0))], plan.Entries);
        Assert.Equal(2, plan.SkippedUnavailable);
    }

    [Fact]
    public void Transfer_Execute_Move_BlanksSourcesAndWritesDestinations()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        Put(sav, 0, 1, 4);
        var useCase = new BulkSlotTransferUseCase();
        var plan = useCase.Plan(sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1)], SlotLocation.FromBox(3, 0), clone: false);

        useCase.Execute(sav, plan.Entries, clone: false);

        Assert.Equal(0, At(sav, 0, 0));
        Assert.Equal(0, At(sav, 0, 1));
        Assert.Equal(1, At(sav, 3, 0));
        Assert.Equal(4, At(sav, 3, 1));
    }

    [Fact]
    public void Transfer_Execute_Copy_KeepsSources()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        var useCase = new BulkSlotTransferUseCase();
        var plan = useCase.Plan(sav, [SlotLocation.FromBox(0, 0)], SlotLocation.FromBox(0, 0), clone: true);

        useCase.Execute(sav, plan.Entries, clone: true);

        Assert.Equal(1, At(sav, 0, 0));
        Assert.Equal(1, At(sav, 0, 1));
    }

    [Fact]
    public void Transfer_MovingPartyMembersOut_CompactsTheRemainingParty()
    {
        var sav = NewSave();
        sav.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        sav.SetPartySlotAtIndex(new PK6 { Species = 2 }, 1);
        sav.SetPartySlotAtIndex(new PK6 { Species = 3 }, 2);
        sav.SetPartySlotAtIndex(new PK6 { Species = 4 }, 3);
        var useCase = new BulkSlotTransferUseCase();
        var plan = useCase.Plan(sav, [SlotLocation.FromParty(0), SlotLocation.FromParty(2)], SlotLocation.FromBox(0, 0), clone: false);

        useCase.Execute(sav, plan.Entries, clone: false);

        Assert.Equal(2, sav.PartyCount);
        Assert.Equal(2, sav.GetPartySlotAtIndex(0).Species);
        Assert.Equal(4, sav.GetPartySlotAtIndex(1).Species);
        Assert.Equal(1, At(sav, 0, 0));
        Assert.Equal(3, At(sav, 0, 1));
    }

    [Fact]
    public void Transfer_MoveThatWouldEmptyTheParty_IsRefusedWhole()
    {
        var sav = NewSave();
        sav.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        sav.SetPartySlotAtIndex(new PK6 { Species = 2 }, 1);

        var plan = new BulkSlotTransferUseCase().Plan(
            sav, [SlotLocation.FromParty(0), SlotLocation.FromParty(1)], SlotLocation.FromBox(0, 0), clone: false);

        Assert.True(plan.WouldEmptyParty);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void Transfer_ToParty_FillsTheNextFreePartySlots()
    {
        var sav = NewSave();
        sav.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        Put(sav, 0, 0, 25);
        Put(sav, 0, 1, 26);
        var useCase = new BulkSlotTransferUseCase();
        var plan = useCase.Plan(sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1)], SlotLocation.FromParty(0), clone: false);

        useCase.Execute(sav, plan.Entries, clone: false);

        Assert.Equal(3, sav.PartyCount);
        Assert.Equal(25, sav.GetPartySlotAtIndex(1).Species);
        Assert.Equal(26, sav.GetPartySlotAtIndex(2).Species);
        Assert.Equal(0, At(sav, 0, 0));
    }

    [Fact]
    public void Transfer_DuplicateInPlace_UsesEachSourcesOwnArea()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 1);
        Put(sav, 2, 0, 4);
        var useCase = new BulkSlotTransferUseCase();
        var plan = useCase.PlanDuplicateInPlace(sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(2, 0)]);

        useCase.Execute(sav, plan.Entries, clone: true);

        Assert.Equal(1, At(sav, 0, 1));
        Assert.Equal(4, At(sav, 2, 1));
        Assert.Equal(1, At(sav, 0, 0));
    }

    [Fact]
    public void Transfer_SkipsProtectedDestinations_AndProtectedMoveSources()
    {
        var sav = NewSaveWithBattleTeam(locked: true);
        // Box 0 slots 0..1 are a locked battle team: a move may neither empty them nor fill them.
        sav.SetBoxSlotAtIndex(new PK7 { Species = 1 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 4 }, 1, 0);

        var useCase = new BulkSlotTransferUseCase();
        var moveOut = useCase.Plan(sav, [SlotLocation.FromBox(0, 0)], SlotLocation.FromBox(1, 0), clone: false);
        Assert.Empty(moveOut.Entries);
        Assert.Equal(1, moveOut.SkippedUnavailable);

        var moveIn = useCase.Plan(sav, [SlotLocation.FromBox(1, 0)], SlotLocation.FromBox(0, 0), clone: false);
        Assert.Equal([new BulkTransferEntry(SlotLocation.FromBox(1, 0), SlotLocation.FromBox(0, 2))], moveIn.Entries);
    }

    // ---------------------------------------------------------------- BulkReleaseSlotsUseCase

    [Fact]
    public void Release_ExcludesPartyMarks_AndReleasesBoxMarks()
    {
        var sav = NewSave();
        sav.SetPartySlotAtIndex(new PK6 { Species = 1 }, 0);
        Put(sav, 0, 0, 4);
        var useCase = new BulkReleaseSlotsUseCase();

        var plan = useCase.Plan(sav, [SlotLocation.FromParty(0), SlotLocation.FromBox(0, 0)]);
        useCase.Execute(sav, plan.Released);

        Assert.False(plan.IsRefused);
        Assert.Equal(1, plan.ExcludedParty);
        Assert.Equal([SlotLocation.FromBox(0, 0)], plan.Released);
        Assert.Equal(0, At(sav, 0, 0));
        Assert.Equal(1, sav.GetPartySlotAtIndex(0).Species);
    }

    [Theory]
    [InlineData(true)]  // locked team
    [InlineData(false)] // unlocked battle team members are still overwrite-protected
    public void Release_RefusesWholeBatch_WhenAnyMarkIsProtected(bool locked)
    {
        var sav = NewSaveWithBattleTeam(locked);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 1 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 4 }, 0, 5);

        var plan = new BulkReleaseSlotsUseCase().Plan(sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 5)]);

        Assert.True(plan.IsRefused);
        Assert.Equal(1, plan.Protected);
        Assert.Empty(plan.Released);
    }

    [Fact]
    public void Release_SkipsEmptyMarks()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 4);

        var plan = new BulkReleaseSlotsUseCase().Plan(sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1)]);

        Assert.Equal(1, plan.SkippedEmpty);
        Assert.Single(plan.Released);
    }

    // ---------------------------------------------------------------- BulkExportSlotsUseCase

    [Fact]
    public void Export_WritesOneFilePerOccupiedMark_AndNeverOverwrites()
    {
        var sav = NewSave();
        Put(sav, 0, 0, 25);
        Put(sav, 0, 1, 25);
        var folder = Directory.CreateTempSubdirectory("pkhex-organizer-export-").FullName;
        try
        {
            var pk = sav.GetBoxSlotAtIndex(0, 0);
            var existing = Path.Combine(folder, PathUtil.CleanFileName(pk.FileName));
            File.WriteAllBytes(existing, [1, 2, 3]);

            var result = new BulkExportSlotsUseCase().Execute(
                sav, [SlotLocation.FromBox(0, 0), SlotLocation.FromBox(0, 1), SlotLocation.FromBox(0, 2)], folder);

            Assert.Equal(2, result.Exported);
            Assert.Equal(1, result.Skipped); // the empty mark
            Assert.Equal([1, 2, 3], File.ReadAllBytes(existing));
            Assert.Equal(3, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
