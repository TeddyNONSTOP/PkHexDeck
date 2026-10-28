using System;
using System.Linq;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using Moq;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Nuzlocke report: locations in the order they were reached, the counted encounter per location (the
/// dupes clause skips a family already counted), extra catches, hatched eggs, traded Pokémon and eggs.
/// </summary>
public class NuzlockeReportTests
{
    private const ushort RouteA = 16, RouteB = 17, RouteC = 18;

    private static SAV4Pt NewSave()
    {
        var sav = new SAV4Pt { OT = "LUCAS", TID16 = 12345, SID16 = 54321 };
        return sav;
    }

    private static PK4 Caught(SAV4Pt sav, ushort species, ushort location, int day, byte metLevel = 5, string? ot = null)
    {
        var pk = new PK4
        {
            Species = species,
            CurrentLevel = metLevel,
            MetLevel = metLevel,
            MetLocation = location,
            Version = GameVersion.Pt,
            OriginalTrainerName = ot ?? sav.OT,
            ID32 = ot is null ? sav.ID32 : 999u,
            Language = (int)LanguageID.English,
        };
        pk.MetDate = new DateOnly(2026, 1, day);
        pk.SetDefaultNickname();
        pk.SetUnshiny();
        pk.RefreshChecksum();
        return pk;
    }

    private static string Area(ushort location) => NuzlockeReportUseCase.AreaName(GameInfo.GetLocationName(false, location, 4, 4, GameVersion.Pt));

    [Fact]
    public void CountsTheFirstNewFamilyAtEachLocation_InTheOrderTheyWereReached()
    {
        var sav = NewSave();
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Shinx, RouteA, 9), 0, 0);       // extra at A
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Starly, RouteA, 1), 0, 1);      // A counts
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Staravia, RouteB, 2), 0, 2);    // B: dupe of Starly
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Bidoof, RouteB, 3), 0, 3);      // B: counts instead
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Starly, RouteC, 4), 0, 4);      // C: only dupes
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Abra, RouteC, 5, ot: "GARY"), 0, 5); // traded
        var egg = Caught(sav, (ushort)Species.Togepi, RouteA, 6);
        egg.IsEgg = true;
        egg.RefreshChecksum();
        sav.SetBoxSlotAtIndex(egg, 0, 6);

        var report = new NuzlockeReportUseCase().Build(sav);

        Assert.Equal([Area(RouteA), Area(RouteB), Area(RouteC)], report.Locations.Select(l => l.Name));
        var a = report.Locations[0];
        Assert.Equal((ushort)Species.Starly, a.Counted!.Species);
        Assert.False(a.FirstIsDupe);
        Assert.Equal((ushort)Species.Shinx, Assert.Single(a.Extras).Species);

        var b = report.Locations[1];
        Assert.True(b.FirstIsDupe);
        Assert.Equal((ushort)Species.Staravia, b.First.Species);
        Assert.Equal((ushort)Species.Bidoof, b.Counted!.Species);

        var c = report.Locations[2];
        Assert.True(c.FirstIsDupe);
        Assert.Null(c.Counted);

        Assert.Equal((ushort)Species.Abra, Assert.Single(report.OtherTrainers).Species);
        Assert.Equal(1, report.EggCount);
    }

    [Fact]
    public void HatchedPokemon_AreFiledWhereTheEggWasReceived()
    {
        var sav = NewSave();
        var hatched = Caught(sav, (ushort)Species.Pichu, RouteA, 2);
        hatched.EggLocation = Locations.Daycare4;
        hatched.EggMetDate = new DateOnly(2026, 1, 1);
        hatched.RefreshChecksum();
        sav.SetBoxSlotAtIndex(hatched, 0, 0);

        var location = Assert.Single(new NuzlockeReportUseCase().Build(sav).Locations);

        Assert.Equal(NuzlockeOrigin.Egg, location.Counted!.Origin);
        Assert.Equal(NuzlockeReportUseCase.AreaName(GameInfo.GetLocationName(true, Locations.Daycare4, 4, 4, GameVersion.Pt)), location.Name);
    }

    [Theory]
    [InlineData("Jubilife City (03)", "Jubilife City")]
    [InlineData("Oreburgh Gate (2)", "Oreburgh Gate")]
    [InlineData("Route 212 (North)", "Route 212 (North)")]
    [InlineData("Route 201", "Route 201")]
    public void NumberedSubLocations_MergeIntoTheirArea(string location, string area)
        => Assert.Equal(area, NuzlockeReportUseCase.AreaName(location));

    [Fact]
    public void Families_UseNewerEvolutionData_WhenTheGamesOwnStopsShort()
    {
        // Luminescent Platinum reuses BDSP's evolution data, which knows nothing past the Sinnoh dex.
        Assert.Equal((ushort)Species.Snivy, NuzlockeReportUseCase.GetFamily(EntityContext.Gen8bLumi, (ushort)Species.Serperior, 0));
        Assert.Equal((ushort)Species.Starly, NuzlockeReportUseCase.GetFamily(EntityContext.Gen4, (ushort)Species.Staraptor, 0));
    }

    [Fact]
    public void ViewModel_FiltersToDupesAndShowsTheCountedEncounter()
    {
        var sav = NewSave();
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Starly, RouteA, 1), 0, 0);
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Staravia, RouteB, 2), 0, 1);
        sav.SetBoxSlotAtIndex(Caught(sav, (ushort)Species.Bidoof, RouteB, 3), 0, 2);
        var vm = new NuzlockeReportViewModel(sav, new Mock<PKHeX.Application.Abstractions.ISpriteRenderer>().Object);
        LivingDexHolding? shown = null;
        vm.HoldingActivated += h => shown = h;

        Assert.Equal(2, vm.Rows.Count);
        vm.FilterIndex = (int)NuzlockeReportFilter.Dupes;
        var row = Assert.Single(vm.Rows);
        Assert.Equal(Area(RouteB), row.Location);

        vm.SelectedRow = row;
        vm.ShowSelectedCommand.Execute(null);
        Assert.Equal((0, 2), (shown!.Value.Box, shown.Value.Slot));
    }
}
