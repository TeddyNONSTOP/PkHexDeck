using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Legality;

/// <summary>
/// Luminescent Platinum has no learnset or encounter data in Core, so legality only checks species and form availability.
/// </summary>
public class LumiLegalityTests
{
    static LumiLegalityTests() => TestUtil.InitializeLegality();

    [Theory]
    [InlineData(Species.Cyndaquil, 0)]
    [InlineData(Species.Chandelure, 0)] // post-Gen 4 species added by Lumi
    [InlineData(Species.Sylveon, 0)]
    [InlineData(Species.Pikachu, 0)]
    public void AvailableSpecies_IsValid_AndReportSaysWhatWasNotChecked(Species species, byte form)
    {
        var la = new LegalityAnalysis(new PB8LUMI { Species = (ushort)species, Form = form });

        la.Valid.Should().BeTrue();
        la.Results.Should().Contain(r => r.Result == LegalityCheckResultCode.EncLumiNotChecked);
        la.Report().Should().Contain("only species and form availability are checked");
        la.Report(true).Should().Contain("only species and form availability are checked");
    }

    [Theory]
    [InlineData(Species.Pikachu, 8)]   // cap Pikachu
    [InlineData(Species.Charizard, 1)] // Mega Charizard X
    [InlineData(Species.Snivy, 0)]     // post-Gen 4 species not added by Lumi
    public void UnavailableSpeciesOrForm_IsInvalid(Species species, byte form)
    {
        var la = new LegalityAnalysis(new PB8LUMI { Species = (ushort)species, Form = form });

        la.Valid.Should().BeFalse();
        la.Results.Should().Contain(r => r.Result == LegalityCheckResultCode.EncLumiUnavailable && !r.Valid);
        la.Report().Should().Contain("appears as Ditto");
    }

    [Fact]
    public void RealLuminescentSave_EveryPokemonIsAnalyzed()
    {
        var path = Path.Combine(TestUtil.GetRepoPath(), "savefiles", "LuminescentPlatinum_test.bin");
        if (!File.Exists(path))
            return;
        var sav = SaveUtil.GetSaveFile(File.ReadAllBytes(path));
        sav.Should().BeOfType<SAV8BSLuminescent>();

        var mons = sav!.PartyData.Concat(sav.BoxData).Where(pk => pk.Species != 0).ToList();
        mons.Should().NotBeEmpty();
        foreach (var pk in mons)
        {
            var la = new LegalityAnalysis(pk);
            la.Parsed.Should().BeTrue();
            la.Results.Should().NotContain(r => r.Result == LegalityCheckResultCode.Error);
        }
    }
}
