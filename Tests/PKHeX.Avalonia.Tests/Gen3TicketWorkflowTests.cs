using Moq;
using PKHeX.Core;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Gen3TicketWorkflowTests
{
    [Theory] [MemberData(nameof(Games))]
    public async Task EveryAvailableTicketAndReceiptFlagCommitsWithCorrectGameMap(GameVersion version)
    {
        var save = CreateSave(version);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        foreach (var row in vm.Tickets)
        { row.StageTicketAndRouteCommand.Execute(null); if (row.HasShownFlag) row.Shown = true; if (row.HasReceivedFlag) row.Received = true; }
        await vm.ApplyCommand.ExecuteAsync(null);
        foreach (var definition in Gen3TicketDefinitions.For(save))
        {
            Assert.True(save.GetEventFlag(definition.TravelFlag));
            Assert.Contains(save.Inventory.GetPouch(InventoryType.KeyItems).Items, item => item.Index == definition.ItemId && item.Count == 1);
            if (definition.ShownFlag is { } shown) Assert.True(save.GetEventFlag(shown));
            if (definition.ReceivedFlag is { } received) Assert.True(save.GetEventFlag(received));
        }
    }

    [Fact]
    public async Task CurrentCountKeyAndMalformedUntouchedSlotArePreserved()
    {
        var save = Assert.IsType<SAV3E>(CreateSave(GameVersion.E));
        var bag = save.Inventory; var pouch = bag.GetPouch(InventoryType.KeyItems);
        pouch.Items[8].Index = 65000; pouch.Items[8].Count = 41234; bag.CopyTo(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        var currentBag = save.Inventory;
        save.SmallBlock.SecurityKey = 0x76543210;
        currentBag.UpdateSecurityKey(save.SmallBlock.SecurityKey); currentBag.CopyTo(save);
        var untouched = save.LargeBlock.Inventory.Slice(0x140 + 8 * 4, 4).ToArray();
        await vm.ApplyCommand.ExecuteAsync(null);
        var current = save.Inventory.GetPouch(InventoryType.KeyItems);
        Assert.Contains(current.Items, item => item.Index == 275 && item.Count == 1);
        Assert.Equal(65000, current.Items[8].Index); Assert.Equal(41234, current.Items[8].Count);
        Assert.Equal(untouched, save.LargeBlock.Inventory.Slice(0x140 + 8 * 4, 4).ToArray());
    }

    [Fact]
    public async Task RemovingAndRestoringTicketPreservesOriginalQuantityAndBytes()
    {
        var save = CreateSave(GameVersion.S); var bag = save.Inventory;
        bag.GetPouch(InventoryType.KeyItems).Items[3].Index = 275;
        bag.GetPouch(InventoryType.KeyItems).Items[3].Count = 9; bag.CopyTo(save); save.State.Edited = false;
        var before = Snapshot(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].HasTicket = false; vm.Tickets[0].HasTicket = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
    }

    public static IEnumerable<object[]> Games => new[] { GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG }.Select(version => new object[] { version });
    internal static SAV3 CreateSave(GameVersion version)
    {
        var file = version switch { GameVersion.R or GameVersion.S => "gen3_ruby.sav", GameVersion.E => "gen3_emerald.sav", _ => "gen3_firered.sav" };
        var bytes = File.ReadAllBytes(Path.Combine(SaveFileFixture.FindSaveFilesPath()!, file));
        SAV3 save = version switch { GameVersion.R or GameVersion.S => new SAV3RS(bytes), GameVersion.E => new SAV3E(bytes), _ => new SAV3FRLG(bytes) };
        save.Version = version;
        if (save is SAV3E emerald) emerald.SmallBlock.SecurityKey = 0x12345678;
        if (save is SAV3FRLG kanto) kanto.SmallBlock.SecurityKey = 0x12345678;
        var bag = save.Inventory;
        foreach (var item in bag.GetPouch(InventoryType.KeyItems).Items) { item.Index = 0; item.Count = 0; }
        bag.CopyTo(save);
        foreach (var definition in Gen3TicketDefinitions.For(save))
        {
            save.SetEventFlag(definition.TravelFlag, false);
            if (definition.ShownFlag is { } shown) save.SetEventFlag(shown, false);
            if (definition.ReceivedFlag is { } received) save.SetEventFlag(received, false);
        }
        save.SetEventFlag(17, false); save.SetEventFlag(18, false); save.SetEventFlag(19, false);
        save.State.Edited = false;
        return save;
    }
    internal static byte[][] Snapshot(SAV3 save) => [save.Data.ToArray(), save.Small.ToArray(), save.Large.ToArray(), save.Storage.ToArray()];
    internal static void AssertUnchanged(SAV3 save, byte[][] before)
    { Assert.Equal(before[0], save.Data.ToArray()); Assert.Equal(before[1], save.Small.ToArray()); Assert.Equal(before[2], save.Large.ToArray()); Assert.Equal(before[3], save.Storage.ToArray()); }
    private static Mock<IDialogService> Dialog(bool confirm = true)
    {
        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(confirm);
        return dialog;
    }
    [Theory] [MemberData(nameof(Games))]
    public async Task NamedTicketRouteAndRawFlagStayStagedThenRoundtrip(GameVersion version)
    {
        var save = CreateSave(version); var before = Snapshot(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        var definition = Gen3TicketDefinitions.For(save)[0];
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        if (definition.ShownFlag.HasValue) vm.Tickets[0].Shown = true;
        if (definition.ReceivedFlag.HasValue) vm.Tickets[0].Received = true;
        vm.SelectedFlagIndex = 17; vm.SelectedFlagValue = true;
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(save.State.Edited);
        var bytes = save.Write();
        SAV3 roundtrip = save switch { SAV3RS => new SAV3RS(bytes), SAV3E => new SAV3E(bytes), _ => new SAV3FRLG(bytes) };
        Assert.True(roundtrip.GetEventFlag(definition.TravelFlag));
        Assert.True(roundtrip.GetEventFlag(17));
        var pouch = roundtrip.Inventory.GetPouch(InventoryType.KeyItems);
        Assert.Contains(pouch.Items, item => item.Index == definition.ItemId && item.Count == 1);
        if (definition.ShownFlag is { } shown) Assert.True(roundtrip.GetEventFlag(shown));
        if (definition.ReceivedFlag is { } received) Assert.True(roundtrip.GetEventFlag(received));
    }
    [Theory] [MemberData(nameof(Games))]
    public async Task NoOpApplyPreservesBytesAndEditedFlag(GameVersion version)
    {
        var save = CreateSave(version); var before = Snapshot(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        await vm.ApplyCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
    }
    [Theory] [MemberData(nameof(Games))]
    public async Task DeclinedBackupConfirmationAndResetDiscardChanges(GameVersion version)
    {
        var save = CreateSave(version); var before = Snapshot(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog(false).Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.CanApply); AssertUnchanged(save, before);
        vm.ResetCommand.Execute(null);
        Assert.False(vm.Tickets[0].HasTicket); Assert.False(vm.Tickets[0].RouteEnabled);
        AssertUnchanged(save, before);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CancelAndWindowDisposePreventLaterWrites(bool cancel)
    {
        var save = CreateSave(GameVersion.E); var before = Snapshot(save);
        var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        if (cancel) vm.CancelCommand.Execute(null); else vm.Dispose();
        vm.Tickets[1].HasTicket = true; await vm.ApplyCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.False(save.State.Edited); Assert.False(vm.CanApply);
    }
    [Fact]
    public void FullPocketCannotPartiallyStageRecipe()
    {
        var save = CreateSave(GameVersion.E); var bag = save.Inventory;
        foreach (var item in bag.GetPouch(InventoryType.KeyItems).Items) { item.Index = 259; item.Count = 1; }
        bag.CopyTo(save); var before = Snapshot(save);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        Assert.False(vm.Tickets[0].RouteEnabled); Assert.False(vm.Tickets[0].HasTicket);
        Assert.NotEmpty(vm.Error); AssertUnchanged(save, before);
    }
    [Fact]
    public async Task InventoryConflictPreventsFlagWrites()
    {
        var save = CreateSave(GameVersion.E);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        var bag = save.Inventory; bag.GetPouch(InventoryType.KeyItems).Items[0].Index = 259;
        bag.GetPouch(InventoryType.KeyItems).Items[0].Count = 1; bag.CopyTo(save);
        var before = Snapshot(save);
        await vm.ApplyCommand.ExecuteAsync(null);
        AssertUnchanged(save, before); Assert.NotEmpty(vm.Error); Assert.True(vm.CanApply);
    }
    [Fact]
    public async Task IndependentFlagAndOtherItemChangesSurviveCommit()
    {
        var save = CreateSave(GameVersion.FR);
        using var vm = new Gen3TicketEditorViewModel(save, Dialog().Object);
        vm.Tickets[0].StageTicketAndRouteCommand.Execute(null);
        save.SetEventFlag(18, true);
        var bag = save.Inventory; bag.GetPouch(InventoryType.KeyItems).Items[5].Index = 259;
        bag.GetPouch(InventoryType.KeyItems).Items[5].Count = 1; bag.CopyTo(save);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(save.GetEventFlag(18)); Assert.Equal(259, save.Inventory.GetPouch(InventoryType.KeyItems).Items[5].Index);
        Assert.True(save.GetEventFlag(0x84A));
    }
}
