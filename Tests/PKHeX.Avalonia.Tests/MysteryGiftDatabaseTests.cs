using Moq;
using PKHeX.Presentation.ViewModels;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Behavioral tests for MysteryGiftDatabaseViewModel's internal-database browse, in particular the
/// Show Items / Show Pokémon toggles. EncounterEvent.GetAllEvents pre-filters to Pokémon only, so
/// item gifts used to never surface no matter what Show Items was set to.
/// </summary>
public class MysteryGiftDatabaseTests
{
    private static MysteryGiftDatabaseViewModel CreateViewModel()
        => new(BlankSaveFile.Get(GameVersion.SL), new Mock<ISpriteRenderer>().Object, new Mock<IDialogService>().Object);

    [Fact]
    public void InternalGifts_IncludeItemGifts()
    {
        var gifts = MysteryGiftDatabaseViewModel.GetInternalGifts().ToList();

        Assert.Contains(gifts, g => g.IsItem);
        Assert.Contains(gifts, g => g.IsEntity);
        Assert.All(gifts, g => Assert.True(g.IsItem || g is { IsEntity: true, Species: not 0 }));
    }

    [Fact]
    public void InternalGifts_PokemonMatchGetAllEvents()
    {
        var expected = EncounterEvent.GetAllEvents(sorted: false).Count();
        var actual = MysteryGiftDatabaseViewModel.GetInternalGifts().Count(g => g.IsEntity);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Search_Default_ShowsBothPokemonAndItems()
    {
        var vm = CreateViewModel();

        Assert.Contains(vm.Results, r => r.Gift.IsItem);
        Assert.Contains(vm.Results, r => r.Gift.IsEntity);
    }

    [Fact]
    public void Search_ShowItemsOff_HidesItems()
    {
        var vm = CreateViewModel();
        vm.ShowItems = false;
        vm.SearchDatabaseCommand.Execute(null);

        Assert.NotEmpty(vm.Results);
        Assert.DoesNotContain(vm.Results, r => r.Gift.IsItem);
    }

    [Fact]
    public void Search_ShowPokemonOff_ShowsOnlyItems()
    {
        var vm = CreateViewModel();
        vm.ShowPokemon = false;
        vm.SearchDatabaseCommand.Execute(null);

        Assert.NotEmpty(vm.Results);
        Assert.All(vm.Results, r => Assert.True(r.Gift.IsItem));
    }

    [Fact]
    public void ItemEntries_HaveItemNamesAndType()
    {
        var vm = CreateViewModel();
        var items = vm.Results.Where(r => r.Gift.IsItem).ToList();

        Assert.All(items, r =>
        {
            Assert.Equal("Item", r.Type);
            Assert.Equal("-", r.Level);
            Assert.Null(r.Sprite);
            _ = r.SpeciesName; // must not throw for any bundled item ID
        });
    }

    [Fact]
    public void SelectGift_ItemGift_DoesNotRaiseGiftSelected()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.GiftSelected += _ => raised = true;

        vm.SelectGiftCommand.Execute(vm.Results.First(r => r.Gift.IsItem));

        Assert.False(raised);
    }
}
