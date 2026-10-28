using CommunityToolkit.Mvvm.Input;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Infrastructure.AutoLegality;
using PKHeX.Presentation.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Covers the Batch Editor's "Max Contest Stats" and "Relearn Moves" quick actions
/// (<see cref="BatchEditorViewModel.MaxContestStatsCommand"/>/<see cref="BatchEditorViewModel.RelearnMovesCommand"/>).
/// Both route through Core's legality-aware suggestion mods (<c>SetMaxContestStats</c>/<c>SetRelearnMoves</c>),
/// which need a real resolvable encounter to do anything meaningful - a bare synthetic PKM resolves to
/// <c>EncounterInvalid</c> and correctly no-ops, so these use <see cref="AutoLegalityService"/> (same
/// engine backing the app's own Showdown-import feature) to build a genuinely legal starting Pokémon.
///
/// Shares <see cref="AutoLegalityServiceTests"/>'s collection since the vendored engine exposes
/// process-wide static settings.
/// </summary>
[Collection("AutoLegality")]
public class BatchEditorLegalQuickActionsTests
{
    private readonly ITestOutputHelper output;
    private readonly IAutoLegalityService _service = new AutoLegalityService();
    private static Mock<IDialogService> DialogMock() => new();

    public BatchEditorLegalQuickActionsTests(ITestOutputHelper output)
    {
        this.output = output;
        GameInfo.CurrentLanguage = "en";
        GameInfo.Strings = GameInfo.GetStrings("en");
    }

    private (SaveFile Sav, PKM Legal) MakeLegalBunnelby()
    {
        var sav = BlankSaveFile.Get(GameVersion.X);
        var result = _service.TryLegalizeShowdownSet(sav, "Bunnelby");
        Assert.True(result.Success, $"Setup failure: {result.Status} {result.MessageText}");
        var pk = result.Pokemon!;
        sav.SetBoxSlotAtIndex(pk, 0);
        return (sav, sav.GetBoxSlotAtIndex(0, 0));
    }

    [Fact]
    public async Task MaxContestStats_MaxesMainFiveStats_OnALegalEncounter()
    {
        var (sav, legal) = MakeLegalBunnelby();
        Assert.True(new LegalityAnalysis(legal).Valid, "Setup fixture must be legal to begin with.");

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        await ((IAsyncRelayCommand)vm.MaxContestStatsCommand).ExecuteAsync(null);

        var result = (IContestStats)sav.GetBoxSlotAtIndex(0, 0);
        Assert.Equal(255, result.ContestCool);
        Assert.Equal(255, result.ContestBeauty);
        Assert.Equal(255, result.ContestCute);
        Assert.Equal(255, result.ContestSmart);
        Assert.Equal(255, result.ContestTough);
        Assert.False(string.IsNullOrEmpty(vm.Results));
        output.WriteLine($"MaxContestStats: Cool/Beauty/Cute/Smart/Tough all 255, Sheen={result.ContestSheen} ✓");
    }

    [Fact]
    public async Task RelearnMoves_PopulatesLegalRelearnMoves_OnAnEggOriginEncounter()
    {
        // A plain wild encounter (e.g. Bunnelby) legally has all-zero relearn moves both before and
        // after - nothing to observe. An egg-move set forces an egg-origin encounter whose relearn
        // moves must include the actual current moves that aren't learned by level-up, giving a real
        // before/after difference to assert on.
        var sav = BlankSaveFile.Get(GameVersion.X);
        var result = _service.TryLegalizeShowdownSet(sav, "Eevee\n- Wish\n- Yawn\n- Tackle\n- Growl");
        Assert.True(result.Success, $"Setup failure: {result.Status} {result.MessageText}");
        var legal = result.Pokemon!;
        Assert.True(new LegalityAnalysis(legal).Valid, "Setup fixture must be legal to begin with.");
        sav.SetBoxSlotAtIndex(legal, 0);

        // Blank out the relearn moves the legalizer set, so the quick action has something to restore.
        var target = sav.GetBoxSlotAtIndex(0, 0);
        target.RelearnMove1 = target.RelearnMove2 = target.RelearnMove3 = target.RelearnMove4 = 0;
        sav.SetBoxSlotAtIndex(target, 0);

        var vm = new BatchEditorViewModel(sav, DialogMock().Object);
        await ((IAsyncRelayCommand)vm.RelearnMovesCommand).ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.Results));
        var final = sav.GetBoxSlotAtIndex(0, 0);
        var la = new LegalityAnalysis(final);
        Assert.True(la.Valid, "Relearn moves must be legal after the batch action:\n" + la.Report());
        Assert.True(final.RelearnMove1 != 0 || final.RelearnMove2 != 0 || final.RelearnMove3 != 0 || final.RelearnMove4 != 0,
            "Expected the egg-origin Eevee's relearn moves to be repopulated, not left blank.");
        output.WriteLine($"RelearnMoves: [{final.RelearnMove1},{final.RelearnMove2},{final.RelearnMove3},{final.RelearnMove4}] ✓");
    }
}
