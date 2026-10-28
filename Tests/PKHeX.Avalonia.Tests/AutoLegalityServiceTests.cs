using System;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Infrastructure;
using PKHeX.Infrastructure.AutoLegality;
using Xunit;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Tests for <see cref="IAutoLegalityService"/> (issue #89 Phase 1): a pasted Showdown set becomes a
/// legal Pokémon for the loaded save's generation/format, and impossible/garbage input fails cleanly.
///
/// All tests live in one class so they run serially: the vendored engine exposes process-wide static
/// settings (e.g. APILegality.Timeout), and serial execution keeps that state stable.
/// </summary>
[Collection("AutoLegality")]
public class AutoLegalityServiceTests
{
    private readonly IAutoLegalityService _service = new AutoLegalityService();

    public AutoLegalityServiceTests()
    {
        GameInfo.CurrentLanguage = "en";
        GameInfo.Strings = GameInfo.GetStrings("en");
    }

    // Representative early-route wild Pokémon per generation: each is obtainable in the named game, so
    // the engine can find a real encounter and produce a legal result quickly.
    [Theory]
    [InlineData(GameVersion.C, "Sentret")]     // Gen 2
    [InlineData(GameVersion.E, "Zigzagoon")]   // Gen 3
    [InlineData(GameVersion.D, "Bidoof")]      // Gen 4
    [InlineData(GameVersion.B, "Patrat")]      // Gen 5
    [InlineData(GameVersion.X, "Bunnelby")]    // Gen 6
    [InlineData(GameVersion.SN, "Pikipek")]    // Gen 7
    [InlineData(GameVersion.SW, "Wooloo")]     // Gen 8
    [InlineData(GameVersion.SL, "Lechonk")]    // Gen 9
    public void TryLegalizeShowdownSet_GeneratesLegalPokemon(GameVersion version, string showdown)
    {
        var sav = BlankSaveFile.Get(version);

        var result = _service.TryLegalizeShowdownSet(sav, showdown);

        Assert.True(result.Success,
            $"Expected a legal result for '{showdown}' in {version}. Status={result.Status}. {result.MessageText}");
        Assert.NotNull(result.Pokemon);
        Assert.Equal(LegalizationStatus.Success, result.Status);

        // The service contract: a successful result MUST independently pass legality analysis.
        var la = new LegalityAnalysis(result.Pokemon!);
        Assert.True(la.Valid, "Generated Pokémon failed LegalityAnalysis:\n" + la.Report());
    }

    [Fact]
    public void TryLegalizeShowdownSet_RespectsRequestedDetails()
    {
        var sav = BlankSaveFile.Get(GameVersion.SW);
        const string set = "Wooloo\nLevel: 15\nAdamant Nature\n- Tackle";

        var result = _service.TryLegalizeShowdownSet(sav, set);

        Assert.True(result.Success, result.MessageText);
        var pk = result.Pokemon!;
        Assert.Equal((ushort)831, pk.Species); // Wooloo
        Assert.Equal(15, pk.CurrentLevel);
        Assert.Equal((int)Nature.Adamant, (int)pk.Nature);
        Assert.True(new LegalityAnalysis(pk).Valid);
    }

    [Fact]
    public void TryLegalizeShowdownSet_ReportsIsPopulatedOnSuccess()
    {
        var sav = BlankSaveFile.Get(GameVersion.SL);

        var result = _service.TryLegalizeShowdownSet(sav, "Lechonk");

        Assert.True(result.Success, result.MessageText);
        Assert.False(string.IsNullOrWhiteSpace(result.LegalityReport));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryLegalizeShowdownSet_EmptyInput_ReturnsInvalidSet(string? text)
    {
        var sav = BlankSaveFile.Get(GameVersion.SW);

        var result = _service.TryLegalizeShowdownSet(sav, text);

        Assert.False(result.Success);
        Assert.Equal(LegalizationStatus.InvalidSet, result.Status);
        Assert.Null(result.Pokemon);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public void TryLegalizeShowdownSet_GarbageText_ReturnsInvalidSet()
    {
        var sav = BlankSaveFile.Get(GameVersion.SW);

        var result = _service.TryLegalizeShowdownSet(sav, "this is not a pokemon set at all !!!");

        Assert.False(result.Success);
        Assert.Equal(LegalizationStatus.InvalidSet, result.Status);
        Assert.Null(result.Pokemon);
    }

    [Fact]
    public void TryLegalizeShowdownSet_ImpossibleSpeciesForGeneration_FailsWithoutCrash()
    {
        // A Gen 9 legendary requested inside a Gen 3 (Emerald) save: there is no encounter that can
        // produce it in that format, so the engine must report a non-success status rather than throw.
        var sav = BlankSaveFile.Get(GameVersion.E);

        var result = _service.TryLegalizeShowdownSet(sav, "Koraidon");

        Assert.False(result.Success);
        Assert.Null(result.Pokemon);
        Assert.Contains(result.Status,
            new[] { LegalizationStatus.Failed, LegalizationStatus.Timeout, LegalizationStatus.InvalidSet });
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public void TryLegalizeShowdownSet_NullSave_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.TryLegalizeShowdownSet(null!, "Pikachu"));
    }

    [Fact]
    public void AutoLegalityService_IsRegisteredInInfrastructure()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IAutoLegalityService>();

        Assert.NotNull(resolved);
        Assert.IsType<AutoLegalityService>(resolved);
    }

    [Fact]
    public void SlotService_IsRegisteredInInfrastructure()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ISlotService));

        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SlotService>(provider.GetRequiredService<ISlotService>());
    }

    // -----------------------------------------------------------------------
    // TryLegalizeExisting (issue #89 Gap A): legalize an already-loaded, possibly illegal Pokémon
    // in place, rather than building a new one from a pasted Showdown set.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a known-illegal Pokémon: start from a legal, engine-generated Bidoof, then corrupt its
    /// Met Level to be higher than its Current Level. "Current level is less than met level" is a
    /// stable, generation-agnostic illegality check in <see cref="LegalityAnalysis"/>. Met/encounter
    /// data is not part of a Showdown set (species/moves/stats/nature only) and is instead supplied
    /// fresh by whichever encounter the engine matches, so this corruption is reliably fixed by
    /// regeneration regardless of which specific encounter the engine happens to pick.
    /// </summary>
    private PKM GetKnownIllegalPokemon(SaveFile sav)
    {
        var baseline = _service.TryLegalizeShowdownSet(sav, "Bidoof\nLevel: 5");
        Assert.True(baseline.Success, baseline.MessageText);

        var pk = baseline.Pokemon!;
        pk.MetLevel = 100;
        Assert.False(new LegalityAnalysis(pk).Valid, "Test setup failure: the corrupted Pokémon should already be illegal.");
        return pk;
    }

    [Fact]
    public void TryLegalizeExisting_FixesKnownIllegalPokemon_BecomesLegal()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var illegal = GetKnownIllegalPokemon(sav);

        var result = _service.TryLegalizeExisting(sav, illegal);

        Assert.True(result.Success, $"Status={result.Status}. {result.MessageText}");
        Assert.NotNull(result.Pokemon);
        Assert.Equal(LegalizationStatus.Success, result.Status);

        // The service contract: a successful result MUST independently pass legality analysis.
        var la = new LegalityAnalysis(result.Pokemon!);
        Assert.True(la.Valid, "Legalized Pokémon failed LegalityAnalysis:\n" + la.Report());
    }

    [Fact]
    public void TryLegalizeExisting_DoesNotMutateTheCallersPokemon()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var illegal = GetKnownIllegalPokemon(sav);
        var before = illegal.Data.ToArray();

        var result = _service.TryLegalizeExisting(sav, illegal);

        Assert.True(result.Success, result.MessageText);
        Assert.NotSame(illegal, result.Pokemon);
        Assert.Equal(before, illegal.Data.ToArray());
    }

    [Fact]
    public void TryLegalizeExisting_EmptySlot_ReturnsInvalidSet()
    {
        var sav = BlankSaveFile.Get(GameVersion.SW);
        var blank = sav.BlankPKM;

        var result = _service.TryLegalizeExisting(sav, blank);

        Assert.False(result.Success);
        Assert.Equal(LegalizationStatus.InvalidSet, result.Status);
        Assert.Null(result.Pokemon);
    }

    [Fact]
    public void TryLegalizeExisting_LuminescentPlatinum_ReturnsUnsupportedWithoutMutating()
    {
        var sav = new SAV8BSLuminescent();
        var pk = new PB8LUMI { Species = (ushort)Species.Pikachu, Form = 8 }; // unavailable form: illegal
        var before = pk.Data.ToArray();

        var result = _service.TryLegalizeExisting(sav, pk);

        Assert.False(result.Success);
        Assert.Equal(LegalizationStatus.Unsupported, result.Status);
        Assert.Null(result.Pokemon);
        Assert.Contains("Luminescent Platinum", result.MessageText);
        Assert.Equal(before, pk.Data.ToArray());
    }

    [Fact]
    public void TryLegalizeExisting_NullSave_Throws()
    {
        var pk = BlankSaveFile.Get(GameVersion.SW).BlankPKM;
        Assert.Throws<ArgumentNullException>(() => _service.TryLegalizeExisting(null!, pk));
    }

    [Fact]
    public void TryLegalizeExisting_NullPokemon_Throws()
    {
        var sav = BlankSaveFile.Get(GameVersion.SW);
        Assert.Throws<ArgumentNullException>(() => _service.TryLegalizeExisting(sav, null!));
    }
}
