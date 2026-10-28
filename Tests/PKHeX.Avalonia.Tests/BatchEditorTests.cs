using CommunityToolkit.Mvvm.Input;
using Moq;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Presentation.ViewModels;
using PKHeX.Core;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Behavioral tests for BatchEditorViewModel.
/// The batch editor parses text instructions and applies them to all non-empty
/// PKM in boxes/party. RunBatchAsync is async; tests await IAsyncRelayCommand.
/// </summary>
public class BatchEditorTests(ITestOutputHelper output)
{
    private static Mock<IDialogService> DialogMock() => new();

    // -----------------------------------------------------------------------
    // 1. PropertySuggestions is populated with PKM properties
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_PropertySuggestions_Populated()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        Assert.NotEmpty(vm.PropertySuggestions);
        Assert.Contains("Species",      vm.PropertySuggestions);
        Assert.Contains("Nickname",     vm.PropertySuggestions);
        Assert.Contains("CurrentLevel", vm.PropertySuggestions);
        output.WriteLine($"PropertySuggestions: {vm.PropertySuggestions.Count} entries ✓");
    }

    // -----------------------------------------------------------------------
    // 2. Priority properties appear first in PropertySuggestions
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_PropertySuggestions_PriorityFirst()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        // "Species" is first in the priority list
        Assert.Equal("Species", vm.PropertySuggestions[0]);
        output.WriteLine("PropertySuggestions: Species is first ✓");
    }

    // -----------------------------------------------------------------------
    // 3. AddInstruction builds correct instruction string
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_AddInstruction_BuildsCorrectString()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.SelectedProperty = "CurrentLevel";
        vm.SelectedOperator = "=";
        vm.SelectedValue    = "50";

        vm.AddInstructionCommand.Execute(null);

        Assert.Equal(".CurrentLevel=50", vm.Instructions);
        output.WriteLine($"AddInstruction: '{vm.Instructions}' ✓");
    }

    // -----------------------------------------------------------------------
    // 4. Multiple AddInstruction calls join with newlines
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_AddInstruction_MultipleLines_JoinedWithNewline()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.SelectedProperty = "CurrentLevel";
        vm.SelectedOperator = "=";
        vm.SelectedValue    = "50";
        vm.AddInstructionCommand.Execute(null);

        vm.SelectedProperty = "IsShiny";
        vm.SelectedOperator = "=";
        vm.SelectedValue    = "true";
        vm.AddInstructionCommand.Execute(null);

        var lines = vm.Instructions.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(".CurrentLevel=50", lines[0]);
        Assert.Equal(".IsShiny=true", lines[1]);
        output.WriteLine($"AddInstruction x2: '{vm.Instructions.Replace(Environment.NewLine, "|")}' ✓");
    }

    // -----------------------------------------------------------------------
    // 5. AddFilter builds correct filter string (= prefix)
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_AddFilter_BuildsCorrectString()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.SelectedProperty = "Species";
        vm.SelectedOperator = "=";
        vm.SelectedValue    = "25";

        vm.AddFilterCommand.Execute(null);

        Assert.Equal("=Species=25", vm.Instructions);
        output.WriteLine($"AddFilter: '{vm.Instructions}' ✓");
    }

    // -----------------------------------------------------------------------
    // 6. ClearInstructions empties both Instructions and Results
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_ClearInstructions_ClearsAll()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.Instructions = ".Level=50";
        vm.Results = "some results";

        vm.ClearInstructionsCommand.Execute(null);

        Assert.Empty(vm.Instructions);
        Assert.Empty(vm.Results);
        output.WriteLine("ClearInstructions: Instructions and Results cleared ✓");
    }

    // -----------------------------------------------------------------------
    // 7. RunBatch with empty instructions is gated off
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_RunBatch_EmptyInstructions_IsDisabled()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.Instructions = string.Empty;
        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        Assert.False(vm.RunBatchCommand.CanExecute(null));
        Assert.Empty(vm.Results);
        output.WriteLine("RunBatch(empty): command disabled ✓");
    }

    // -----------------------------------------------------------------------
    // 8. RunBatch processes PKM in boxes (Gen6)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_RunBatch_ModifiesBoxPokemon()
    {
        var sav = new SAV6XY();

        // Inject a PK6 into box 0
        var pk = new PK6 { Species = 1 }; pk.CurrentLevel = 5;
        sav.SetBoxSlotAtIndex(pk, 0);

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditBoxes = true;
        vm.EditParty = false;
        vm.Instructions = ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 1);

        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        var resultPk = sav.GetBoxSlotAtIndex(0, 0);
        Assert.Equal(50, resultPk.CurrentLevel);
        Assert.False(string.IsNullOrEmpty(vm.Results));
        output.WriteLine($"RunBatch: Bulbasaur Level 5→50 ✓ Results='{vm.Results}'");
    }

    // -----------------------------------------------------------------------
    // 8b. Quick action: max friendship (no legal-encounter dependency)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_MaxFriendship_SetsCurrentFriendshipTo255()
    {
        var sav = new SAV6XY();
        var pk = new PK6 { Species = 1, CurrentFriendship = 0 };
        sav.SetBoxSlotAtIndex(pk, 0);

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        await WaitForAsync(() => vm.MaxFriendshipCommand.CanExecute(null));

        await ((IAsyncRelayCommand)vm.MaxFriendshipCommand).ExecuteAsync(null);

        Assert.Equal(255, sav.GetBoxSlotAtIndex(0, 0).CurrentFriendship);
        output.WriteLine("MaxFriendship: CurrentFriendship 0→255 ✓");
    }

    // MaxContestStats and RelearnMoves are covered in BatchEditorLegalQuickActionsTests: both are
    // legality-aware (SetMaxContestStats/SetRelearnMoves need a real resolvable encounter, not just a
    // synthetic species+level entity - a raw PK6 here resolves to EncounterInvalid and correctly does
    // nothing, which would make an in-place test here misleadingly look broken).

    // -----------------------------------------------------------------------
    // 9. BatchEditCompleted event fires after successful batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_RunBatch_FiresBatchEditCompleted()
    {
        var sav = new SAV6XY();
        var pk = new PK6 { Species = 25 }; pk.CurrentLevel = 5;
        sav.SetBoxSlotAtIndex(pk, 0);

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.Instructions = ".Nickname=Pika";
        await WaitForAsync(() => vm.AffectedCount == 1);

        bool eventFired = false;
        vm.BatchEditCompleted += () => eventFired = true;

        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        Assert.True(eventFired, "BatchEditCompleted should fire after successful batch");
        output.WriteLine("BatchEditCompleted event fired ✓");
    }

    [Fact]
    public async Task BatchEditor_AffectedCount_TracksCurrentInstructionsAndTargets()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25, CurrentLevel = 5 }, 0, 1);

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.Instructions = "=Species=1" + Environment.NewLine + ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 1);

        Assert.Equal(1, vm.AffectedCount);
        Assert.True(vm.RunBatchCommand.CanExecute(null));

        vm.Instructions = "=Species=999" + Environment.NewLine + ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 0);

        Assert.Equal(0, vm.AffectedCount);
        Assert.False(vm.RunBatchCommand.CanExecute(null));

        vm.EditBoxes = false;
        vm.EditParty = true;
        await WaitForAsync(() => vm.AffectedCount == 0);

        Assert.Equal(0, vm.AffectedCount);
        Assert.False(vm.RunBatchCommand.CanExecute(null));
    }

    [Fact]
    public void BatchEditor_QuickActions_AreGatedByTheirOwnAffectedCount()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1 }, 0, 0);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        Assert.True(vm.SetShinyCommand.CanExecute(null));

        vm.EditBoxes = false;
        vm.EditParty = false;

        Assert.False(vm.SetShinyCommand.CanExecute(null));
    }

    [Fact]
    public async Task BatchEditor_RunBatch_IsOneUndoableOperation()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25, CurrentLevel = 5 }, 0, 1);
        var undoRedo = new UndoRedoService();
        undoRedo.Initialize(sav);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object, undoRedo)
        {
            Instructions = ".CurrentLevel=50",
        };
        await WaitForAsync(() => vm.AffectedCount == 2);

        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        Assert.Equal(50, sav.GetBoxSlotAtIndex(0, 0).CurrentLevel);
        Assert.Equal(50, sav.GetBoxSlotAtIndex(0, 1).CurrentLevel);
        Assert.True(undoRedo.CanUndo);

        undoRedo.Undo();

        Assert.Equal(5, sav.GetBoxSlotAtIndex(0, 0).CurrentLevel);
        Assert.Equal(5, sav.GetBoxSlotAtIndex(0, 1).CurrentLevel);
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public async Task BatchEditor_ResetRestoresStateAndDiscardsBatchUndo()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        var undoRedo = new UndoRedoService();
        undoRedo.Initialize(sav);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object, undoRedo)
        {
            Instructions = ".CurrentLevel=50",
        };
        await WaitForAsync(() => vm.AffectedCount == 1);

        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);
        vm.ResetBatchCommand.Execute(null);

        Assert.Equal(5, sav.GetBoxSlotAtIndex(0, 0).CurrentLevel);
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public async Task BatchEditor_SharedUndoRedo_RefreshesOtherEditorPreview()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        var undoRedo = new UndoRedoService();
        undoRedo.Initialize(sav);
        var vm1 = new BatchEditorViewModel(sav, DialogMock().Object, undoRedo)
        {
            Instructions = "=CurrentLevel=5" + Environment.NewLine + ".CurrentLevel=50",
        };
        var vm2 = new BatchEditorViewModel(sav, DialogMock().Object, undoRedo)
        {
            Instructions = "=CurrentLevel=5" + Environment.NewLine + ".CurrentLevel=50",
        };

        await WaitForAsync(() => vm1.AffectedCount == 1 && vm2.AffectedCount == 1);
        await ((IAsyncRelayCommand)vm1.RunBatchCommand).ExecuteAsync(null);
        Assert.Equal(50, sav.GetBoxSlotAtIndex(0, 0).CurrentLevel);
        await WaitForAsync(() => vm2.AffectedCount == 0);

        undoRedo.Undo();
        await WaitForAsync(() => vm2.AffectedCount == 1);
        undoRedo.Redo();
        await WaitForAsync(() => vm2.AffectedCount == 0);
    }

    [Fact]
    public async Task BatchEditor_Preview_UsesLatestInstructions()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25, CurrentLevel = 5 }, 0, 1);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        vm.Instructions = ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 2);
        vm.Instructions = "=Species=999" + Environment.NewLine + ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 0);
    }

    [Fact]
    public async Task BatchEditor_RefreshExternalState_UpdatesPreviewAndQuickActions()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 0, 0);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object)
        {
            Instructions = "=Species=1" + Environment.NewLine + ".CurrentLevel=50",
        };
        await WaitForAsync(() => vm.AffectedCount == 1);

        var commandNotifications = 0;
        vm.SetShinyCommand.CanExecuteChanged += (_, _) => commandNotifications++;
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);
        vm.RefreshExternalState();

        await WaitForAsync(() => vm.AffectedCount == 0);
        Assert.True(commandNotifications > 0);
        Assert.False(vm.SetShinyCommand.CanExecute(null));
    }

    [Fact]
    public void BatchEditor_InstructionBuilderCommands_RequireAProperty()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        Assert.False(vm.AddFilterCommand.CanExecute(null));
        Assert.False(vm.AddInstructionCommand.CanExecute(null));

        vm.SelectedProperty = "Species";
        Assert.True(vm.AddFilterCommand.CanExecute(null));
        Assert.True(vm.AddInstructionCommand.CanExecute(null));

        vm.SelectedProperty = string.Empty;
        Assert.False(vm.AddFilterCommand.CanExecute(null));
        Assert.False(vm.AddInstructionCommand.CanExecute(null));
    }

    // -----------------------------------------------------------------------
    // 11. RunBatch updates the Pokédex (regression: upstream a595033 fixed a
    //     bug where EntityImportSettings.None disabled UpdateToSaveFile and
    //     UpdatePokeDex too, not just UpdateRecord - batch-edited Pokemon never
    //     got marked seen/caught).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_RunBatch_UpdatesPokedex()
    {
        var sav = new SAV4Pt();
        var pk = new PK4 { Species = 1 }; pk.CurrentLevel = 5;
        sav.SetBoxSlotAtIndex(pk, 0, 0, EntityImportSettings.None); // insert without touching the dex
        Assert.False(sav.GetCaught(1));

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.Instructions = ".CurrentLevel=50";
        await WaitForAsync(() => vm.AffectedCount == 1);

        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        Assert.True(sav.GetCaught(1));
        output.WriteLine("RunBatch: Pokedex caught flag updated ✓");
    }

    // -----------------------------------------------------------------------
    // 10. Operators list contains expected operators
    // -----------------------------------------------------------------------

    [Fact]
    public void BatchEditor_Operators_ContainsExpected()
    {
        var sav = new SAV6XY();
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);

        Assert.Contains("=", vm.Operators);
        Assert.Contains("!", vm.Operators);
        output.WriteLine($"Operators: [{string.Join(", ", vm.Operators)}] ✓");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The batch editor preview did not reach the expected state.");

            await Task.Delay(20);
        }
    }

    // -----------------------------------------------------------------------
    // Luminescent Platinum entities are a PB8 subclass that Core's batch editor
    // did not list, so every property lookup on them indexed past its type table.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BatchEditor_RunBatch_EditsLuminescentPlatinumPokemon()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles/LuminescentPlatinum_test.bin"));
        var sav = Assert.IsType<SAV8BSLuminescent>(FileUtil.GetSupportedFile(path));
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditParty = false;
        vm.Instructions = ".Nickname=HCTEST";

        // The Run button only enables once the background preview has counted the affected slots.
        await WaitForAsync(() => vm.AffectedCount > 0);
        await ((IAsyncRelayCommand)vm.RunBatchCommand).ExecuteAsync(null);

        var boxed = Enumerable.Range(0, sav.BoxCount * sav.BoxSlotCount)
            .Select(i => sav.GetBoxSlotAtIndex(i))
            .Where(pk => pk.Species != 0)
            .ToList();
        Assert.NotEmpty(boxed);
        Assert.All(boxed, pk => Assert.Equal("HCTEST", pk.Nickname));
        Assert.DoesNotContain("Error", vm.Results, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // The status line says why Run Batch is unavailable instead of a silent 0.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("", true, "BatchEditor_Status_Empty")]
    [InlineData(".Nickname=HCTEST", false, "BatchEditor_Status_NoTargets")]
    [InlineData("Nickname HCTEST", true, "BatchEditor_Status_Unreadable")]
    [InlineData(".NotAProperty=1", true, "BatchEditor_Status_NothingChanges")]
    [InlineData("=Species=151\n.Nickname=HCTEST", true, "BatchEditor_Status_NothingChanges")]
    public async Task BatchEditor_PreviewStatus_ExplainsWhyRunIsUnavailable(string text, bool boxes, string key)
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditParty = false;
        vm.EditBoxes = boxes;
        vm.Instructions = text;

        var expected = PKHeX.Presentation.Localization.LocalizedStrings.Instance[key];
        await WaitForAsync(() => vm.PreviewStatus == expected);
        Assert.False(vm.RunBatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task BatchEditor_PreviewStatus_SaysHowManyWillChange()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1 }, 1);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditParty = false;
        vm.Instructions = ".Nickname=HCTEST";

        var expected = PKHeX.Presentation.Localization.LocalizedStrings.Instance.Format("BatchEditor_Status_Ready", 2);
        await WaitForAsync(() => vm.PreviewStatus == expected);
        Assert.True(vm.RunBatchCommand.CanExecute(null));
    }

    // -----------------------------------------------------------------------
    // Quick actions must send instructions Core recognizes: unknown properties
    // and suggestion names without a $suggest value are skipped silently.
    // -----------------------------------------------------------------------

    private static async Task<PKM> RunQuickActionAsync(SaveFile sav, Func<BatchEditorViewModel, IAsyncRelayCommand> pick, bool party = false)
    {
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditBoxes = !party;
        vm.EditParty = party;
        await WaitForAsync(() => pick(vm).CanExecute(null));
        await pick(vm).ExecuteAsync(null);
        return party ? sav.GetPartySlotAtIndex(0) : sav.GetBoxSlotAtIndex(0);
    }

    private static SaveFile QuickActionSave(bool lumi)
    {
        if (lumi)
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles/LuminescentPlatinum_test.bin"));
            var lumiSave = (SaveFile)FileUtil.GetSupportedFile(path)!;
            var first = Enumerable.Range(0, lumiSave.BoxCount * lumiSave.BoxSlotCount).First(i => lumiSave.GetBoxSlotAtIndex(i).Species != 0);
            var pk = lumiSave.GetBoxSlotAtIndex(first);
            lumiSave.SetBoxSlotAtIndex(pk, 0); // the action checks slot 0
            return lumiSave;
        }

        var sav = new SAV6XY();
        var pk6 = new PK6 { Species = 25, CurrentLevel = 50, IV_HP = 3, IV_ATK = 3, IV_DEF = 3, IV_SPA = 3, IV_SPD = 3, IV_SPE = 3 };
        pk6.SetUnshiny();
        sav.SetBoxSlotAtIndex(pk6, 0);
        return sav;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickAction_MaxIVs_SetsEveryIVToTheMaximum(bool lumi)
    {
        var sav = QuickActionSave(lumi);
        var pk = await RunQuickActionAsync(sav, vm => vm.SetMaxIVsCommand);
        Assert.Equal(6 * sav.MaxIV, pk.IVTotal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickAction_MakeShiny_MakesItShiny(bool lumi)
    {
        var sav = QuickActionSave(lumi);
        var before = sav.GetBoxSlotAtIndex(0);
        if (before.IsShiny) { before.SetUnshiny(); sav.SetBoxSlotAtIndex(before, 0); }
        var pk = await RunQuickActionAsync(sav, vm => vm.SetShinyCommand);
        Assert.True(pk.IsShiny);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickAction_MaxEVs_RaisesEVs(bool lumi)
    {
        var sav = QuickActionSave(lumi);
        var before = sav.GetBoxSlotAtIndex(0);
        before.SetEVs([0, 0, 0, 0, 0, 0]);
        sav.SetBoxSlotAtIndex(before, 0);
        var pk = await RunQuickActionAsync(sav, vm => vm.SetMaxEVsCommand);
        Assert.True(pk.EVTotal > 0);
    }

    [Fact]
    public async Task QuickAction_HealAll_RestoresHp()
    {
        var sav = new SAV6XY();
        var pk = new PK6 { Species = 25, CurrentLevel = 50 };
        pk.SetUnshiny();
        pk.ResetPartyStats();
        pk.Stat_HPCurrent = 1;
        sav.SetPartySlotAtIndex(pk, 0);

        var healed = await RunQuickActionAsync(sav, vm => vm.HealAllCommand, party: true);

        Assert.Equal(healed.Stat_HPMax, healed.Stat_HPCurrent);
        Assert.True(healed.Stat_HPMax > 1);
    }

    [Fact]
    public async Task QuickAction_AfterRunning_StatusSaysDoneUntilTheInstructionsChange()
    {
        var sav = QuickActionSave(lumi: false);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 1, CurrentLevel = 5 }, 1);
        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        vm.EditParty = false;
        await WaitForAsync(() => vm.SetMaxIVsCommand.CanExecute(null));

        await vm.SetMaxIVsCommand.ExecuteAsync(null);

        var done = PKHeX.Presentation.Localization.LocalizedStrings.Instance.Format("BatchEditor_Status_Done", 2);
        await WaitForAsync(() => vm.PreviewStatus == done);
        Assert.False(vm.RunBatchCommand.CanExecute(null));

        vm.Instructions = ".NotAProperty=1";
        var nothing = PKHeX.Presentation.Localization.LocalizedStrings.Instance["BatchEditor_Status_NothingChanges"];
        await WaitForAsync(() => vm.PreviewStatus == nothing);
    }
}
