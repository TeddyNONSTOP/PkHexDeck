using PKHeX.Core;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class PlaPokedexFoundationTests
{
    [Theory] [InlineData(25)] [InlineData(722)] [InlineData(201)]
    public void AllEightGenderAlphaShinyFlagsRoundtripThroughTheNormalSaveParser(int species)
    {
        var save = LoadSave(); using var vm = new PokedexLAEditorViewModel(save);
        var entry = vm.SpeciesList.Single(entry => entry.Species == species); var form = entry.Forms[0];
        form.Seen0 = form.Seen1 = form.Seen2 = form.Seen3 = form.Seen4 = form.Seen5 = form.Seen6 = form.Seen7 = true;
        form.Obtained0 = form.Obtained1 = form.Obtained2 = form.Obtained3 = form.Obtained4 = form.Obtained5 = form.Obtained6 = form.Obtained7 = true;
        form.Caught0 = form.Caught1 = form.Caught2 = form.Caught3 = form.Caught4 = form.Caught5 = form.Caught6 = form.Caught7 = true;
        entry.DisplayForm = form.Form; entry.DisplayAlpha = true; entry.DisplayShiny = true; entry.DisplayFemale = true;
        var expected = (SAV8LA)save.Clone(); var dex = expected.Blocks.PokedexSave;
        dex.SetPokeSeenInWildFlags((ushort)species, (byte)form.Form, 255); dex.SetPokeObtainFlags((ushort)species, (byte)form.Form, 255);
        dex.SetPokeCaughtInWildFlags((ushort)species, (byte)form.Form, 255); dex.SetSelectedGenderForm((ushort)species, (byte)form.Form, true, true, true);
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        var reopened = Assert.IsType<SAV8LA>(SaveUtil.GetSaveFile(save.Write())); var actual = reopened.Blocks.PokedexSave;
        Assert.Equal(255, actual.GetPokeSeenInWildFlags((ushort)species, (byte)form.Form));
        Assert.Equal(255, actual.GetPokeObtainFlags((ushort)species, (byte)form.Form)); Assert.Equal(255, actual.GetPokeCaughtInWildFlags((ushort)species, (byte)form.Form));
        Assert.Equal(dex.GetSelectedGender1((ushort)species), actual.GetSelectedGender1((ushort)species));
        Assert.True(actual.GetSelectedAlpha((ushort)species)); Assert.True(actual.GetSelectedShiny((ushort)species));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ConfirmedTaskScopeStagesOnlyEditableCountersAndUndoRestoresDrafts(bool whole)
    {
        var save = LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        var dialogs = new Mock<IDialogService>(); string? scope = null;
        dialogs.Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, text, _, _) => scope = text).ReturnsAsync(true);
        using var vm = new PokedexLAEditorViewModel(save, dialogs.Object);
        var target = vm.SpeciesList.Single(entry => entry.Species == 25); vm.SelectedSpecies = target; vm.EntirePokedex = whole;
        target.Forms[0].MinimumHeight = "1"; target.Forms[0].MaximumHeight = "2";
        await vm.ClearTasksCommand.ExecuteAsync(null);
        Assert.Contains(whole ? LocalizedStrings.Instance["Dex9a_WholeDex"] : target.DisplayName, scope);
        Assert.True(vm.CanUndo);
        foreach (var entry in vm.SpeciesList.Where(entry => whole || entry.Species == 25))
            Assert.All(entry.Tasks.Where(task => task.CanEdit), task => Assert.Equal(0, task.CurrentValue));
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.UndoCommand.Execute(null);
        Assert.Equal("1", vm.SpeciesList.Single(entry => entry.Species == 25).Forms[0].MinimumHeight);
        Assert.Equal("2", vm.SpeciesList.Single(entry => entry.Species == 25).Forms[0].MaximumHeight);
        vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
    [Fact]
    public async Task ReportCurrentUsesTheSelectedSpeciesAndLateConfirmationsAreDiscarded()
    {
        var save = LoadSave(); var answer = new TaskCompletionSource<bool>();
        var dialogs = new Mock<IDialogService>(); string? scope = null;
        dialogs.Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, text, _, _) => scope = text).Returns(answer.Task);
        using var vm = new PokedexLAEditorViewModel(save, dialogs.Object);
        var pikachu = vm.SpeciesList.Single(entry => entry.Species == 25); vm.SelectedSpecies = pikachu;
        var pending = pikachu.ReportSpeciesCommand.ExecuteAsync(null); Assert.Contains(pikachu.DisplayName, scope);
        vm.ResetCommand.Execute(null); answer.SetResult(true); await pending; Assert.False(vm.CanUndo);
    }
    internal static SAV8LA LoadSave() => Assert.IsType<SAV8LA>(Fixtures.SaveFileFixture.LoadSave(
        Path.Combine(Fixtures.SaveFileFixture.FindSaveFilesPath()!, "gen8a_legendsarceus.main")));
    [Fact]
    public void AllThirtyCountersStaySynchronizedWithTaskRowsAndRoundtrip()
    {
        var save = LoadSave(); using var vm = new PokedexLAEditorViewModel(save);
        var rowlet = vm.SpeciesList.Single(entry => entry.Species == 722);
        Assert.Equal(30, rowlet.AllCounters.Count);
        var catchCounter = rowlet.AllCounters.Single(counter => counter.Type == PokedexResearchTaskType8a.Catch);
        catchCounter.CurrentValue = 123; Assert.Equal(123, rowlet.Tasks[0].CurrentValue);
        rowlet.Tasks[0].CurrentValue = 321; Assert.Equal(321, catchCounter.CurrentValue);
        save.Blocks.PokedexSave.GetResearchTaskProgressByForce(722, PokedexResearchTaskType8a.Catch, -1, out var original);
        rowlet.Tasks[0].CurrentValue = original; Assert.Equal(original, catchCounter.CurrentValue);
        var fourthMove = rowlet.AllCounters.Single(counter => counter.Type == PokedexResearchTaskType8a.UseMove && counter.Index == 3);
        fourthMove.CurrentValue = 456;
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        var reopened = Assert.IsType<SAV8LA>(SaveUtil.GetSaveFile(save.Write()));
        Assert.True(reopened.Blocks.PokedexSave.GetResearchTaskProgressByForce(722, PokedexResearchTaskType8a.UseMove, 3, out var stored));
        Assert.Equal(456, stored);
        reopened.Blocks.PokedexSave.GetResearchTaskProgressByForce(722, PokedexResearchTaskType8a.Catch, -1, out var obtained);
        Assert.Equal(original, obtained);
    }
    [Fact]
    public void RejectedTransactionRetainsDraftsAndCanRetryAfterTheConflictIsReverted()
    {
        var save = LoadSave(); using var first = new PokedexLAEditorViewModel(save); using var second = new PokedexLAEditorViewModel(save);
        byte flags = save.Blocks.PokedexSave.GetPokeSeenInWildFlags(722, 0);
        first.SpeciesList.Single(entry => entry.Species == 722).Forms[0].Seen0 = (flags & 1) == 0;
        first.SaveCommand.Execute(null); Assert.Empty(first.Error);
        var secondEntry = second.SpeciesList.Single(entry => entry.Species == 25); int desired = secondEntry.Tasks[0].CurrentValue + 1;
        secondEntry.Tasks[0].CurrentValue = desired;
        var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        second.SaveCommand.Execute(null); Assert.NotEmpty(second.Error);
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
        Assert.Equal(desired, secondEntry.Tasks[0].CurrentValue);
        save.Blocks.PokedexSave.SetPokeSeenInWildFlags(722, 0, flags);
        second.SaveCommand.Execute(null); Assert.Empty(second.Error);
        save.Blocks.PokedexSave.GetResearchTaskProgressByForce(25, PokedexResearchTaskType8a.Catch, -1, out var stored); Assert.Equal(desired, stored);
    }
    [Fact]
    public void SearchClearsAndRowletUsesBaseFormAndCoreDelta()
    {
        var save = LoadSave(); using var vm = new PokedexLAEditorViewModel(save); int all = vm.SpeciesList.Count;
        var rowlet = vm.SpeciesList.Single(entry => entry.Name == GameInfo.Strings.Species[722]);
        Assert.Equal(LocalizedStrings.Instance["Dex9a_BaseForm"], rowlet.Forms[0].Name);
        Assert.Equal(190, rowlet.ReportedResearchLevel); Assert.Equal(190, rowlet.UnreportedResearchLevel);
        vm.SearchText = GameInfo.Strings.Species[25]; Assert.Single(vm.SpeciesList); Assert.Same(vm.SpeciesList[0], vm.SelectedSpecies);
        vm.SearchText = "no such species"; Assert.Empty(vm.SpeciesList); Assert.Null(vm.SelectedSpecies);
        vm.SearchText = string.Empty; Assert.Equal(all, vm.SpeciesList.Count);
    }
    [Fact]
    public void NoOpAndDiscardPreserveEverySourceBlock()
    {
        var save = LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new PokedexLAEditorViewModel(save); vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.SpeciesList[0].Tasks[0].CurrentValue++; vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.ReportAllCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
        vm.Dispose(); vm.SaveCommand.Execute(null);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
    [Fact]
    public void RowletTasksUseNativeDescriptionsAndUpdateOnlyTheClone()
    {
        var save = LoadSave(); var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        using var vm = new PokedexLAEditorViewModel(save);
        var rowlet = vm.SpeciesList.Single(entry => entry.Name == GameInfo.Strings.Species[722]);
        var definitions = PokedexConstants8a.ResearchTasks[rowlet.DexIndex - 1];
        Assert.Equal(definitions.Length, rowlet.Tasks.Count);
        for (int i = 0; i < definitions.Length; i++)
        {
            var task = rowlet.Tasks[i];
            Assert.Equal(definitions[i].TaskThresholds, task.Thresholds);
            Assert.Equal(definitions[i].RequiredForCompletion, task.IsRequired);
            Assert.Equal(definitions[i].PointsBonus != 0, task.HasBonus);
            Assert.Equal(definitions[i].GetTaskLabelString(Util.GetStringList("tasks8a", GameInfo.CurrentLanguage),
                Util.GetStringList("time_tasks8a", GameInfo.CurrentLanguage), Util.GetStringList("species_tasks8a", GameInfo.CurrentLanguage)), task.Description);
        }
        var expected = (SAV8LA)save.Clone(); var changed = rowlet.Tasks[0]; changed.CurrentValue = 0;
        expected.Blocks.PokedexSave.SetResearchTaskProgressByForce(722, definitions[0], 0);
        int expectedRate = expected.Blocks.PokedexSave.GetPokeResearchRate(722);
        for (int i = 0; i < definitions.Length; i++) expectedRate += expected.Blocks.PokedexSave.GetResearchTaskLevel(722, i, out _, out _, out _) * (definitions[i].PointsSingle + definitions[i].PointsBonus);
        Assert.Equal(expectedRate, rowlet.UnreportedResearchLevel);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
    [Fact]
    public void SizesAndDisplayStateRoundtripAndInvalidSizesBlockEveryWrite()
    {
        var save = LoadSave(); using var vm = new PokedexLAEditorViewModel(save);
        var rowlet = vm.SpeciesList.Single(entry => entry.Name == GameInfo.Strings.Species[722]);
        var form = rowlet.Forms[0]; form.HasMaximum = true;
        form.MinimumHeight = "1"; form.MaximumHeight = "2"; form.MinimumWeight = "3"; form.MaximumWeight = "4";
        rowlet.DisplayAlpha = !rowlet.DisplayAlpha; rowlet.DisplayShiny = !rowlet.DisplayShiny;
        vm.SaveCommand.Execute(null); Assert.Empty(vm.Error);
        var reopened = Assert.IsType<SAV8LA>(SaveUtil.GetSaveFile(save.Write()));
        Assert.True(reopened.Blocks.PokedexSave.GetSizeStatistics(722, 0, out var both, out var minH, out var maxH, out var minW, out var maxW));
        Assert.True(both); Assert.Equal(1, minH); Assert.Equal(2, maxH); Assert.Equal(3, minW); Assert.Equal(4, maxW);
        Assert.Equal(rowlet.DisplayAlpha, reopened.Blocks.PokedexSave.GetSelectedAlpha(722));
        Assert.Equal(rowlet.DisplayShiny, reopened.Blocks.PokedexSave.GetSelectedShiny(722));
        rowlet = vm.SpeciesList.Single(entry => entry.Name == GameInfo.Strings.Species[722]);
        rowlet.Forms[0].MaximumHeight = "-1"; rowlet.Tasks[0].CurrentValue++;
        var before = save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
        vm.SaveCommand.Execute(null); Assert.NotEmpty(vm.Error);
        foreach (var b in save.AllBlocks) Assert.Equal(before[b.Key], b.Data.ToArray());
    }
}
