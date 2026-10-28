using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class SvRaidWorkflowTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    private static Dictionary<uint, byte[]> Snapshot(SAV9SV save) => save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
    private static void Unchanged(SAV9SV save, Dictionary<uint, byte[]> expected)
    { foreach (var block in save.AllBlocks) Assert.Equal(expected[block.Key], block.Data.ToArray()); }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 2)] [InlineData(2, 3)]
    public void RegionAvailability_FollowsRevision_WithSharedSevenStarSession(int revision, int count)
    {
        using var vm = new Raid9EditorViewModel(SvRaidDataSessionTests.CreateSave(revision));
        Assert.Equal(count, vm.Regions.Count);
        Assert.Equal(72, vm.Regions[0].Raids.Count);
        if (count > 1) Assert.Equal(100, vm.Regions[1].Raids.Count);
        if (count > 2) Assert.Equal(80, vm.Regions[2].Raids.Count);
        Assert.NotEmpty(vm.RaidItems);
        Assert.True(vm.CanSave);
    }
    [Fact]
    public void EveryFieldAndSevenStarRecord_StagesAndRoundTrips_PreservingIndependentEdits()
    {
        var save = SvRaidDataSessionTests.CreateSave();
        var before = Snapshot(save);
        using var vm = new Raid9EditorViewModel(save);
        foreach (var region in vm.Regions)
        {
            vm.SelectedRegion = region;
            var row = vm.SelectedRaid!;
            row.Area = uint.MaxValue; row.LotteryGroup = uint.MaxValue - 1; row.SpawnPointId = 42;
            row.SeedHex = "ABCDEF01"; row.Content = -1; row.LeaguePointsClaimed = true; row.IsEnabled = true;
            Assert.Contains("4294967295", row.ContentName);
            Assert.Contains("4294967295_4294967294_42", row.ScenePointName);
        }
        vm.SelectedRecord!.Identifier = 20260930; vm.SelectedRecord.Captured = true; vm.SelectedRecord.Defeated = true;
        Unchanged(save, before); Assert.False(save.State.Edited);
        save.TID16 = 12345;
        vm.SaveCommand.Execute(null);
        Assert.True(save.State.Edited); Assert.Equal((ushort)12345, save.TID16);
        var reloaded = new SAV9SV(save.Write());
        foreach (var data in new[] { reloaded.RaidPaldea, reloaded.RaidKitakami, reloaded.RaidBlueberry })
        {
            var row = data.GetRaid(0);
            Assert.Equal(uint.MaxValue, row.AreaID); Assert.Equal(uint.MaxValue - 1, row.LotteryGroup);
            Assert.Equal(42u, row.SpawnPointID); Assert.Equal(0xABCDEF01u, row.Seed);
            Assert.Equal(uint.MaxValue, (uint)row.Content); Assert.True(row.IsClaimedLeaguePoints); Assert.True(row.IsEnabled);
        }
        Assert.Equal(20260930u, reloaded.RaidSevenStar.GetRaid(0).Identifier);
        Assert.True(reloaded.RaidSevenStar.GetRaid(0).Captured); Assert.True(reloaded.RaidSevenStar.GetRaid(0).Defeated);
    }
    [Theory]
    [InlineData("cancel")] [InlineData("close")] [InlineData("reset")]
    public void AllChanges_CanBeDiscarded_WithoutChangingSource(string action)
    {
        var save = SvRaidDataSessionTests.CreateSave(); var before = Snapshot(save);
        using var vm = new Raid9EditorViewModel(save);
        foreach (var region in vm.Regions) region.Raids[0].Seed = 12345;
        vm.RaidItems[0].Identifier = 20260930; vm.RaidItems[0].Captured = true;
        if (action == "cancel") vm.CancelCommand.Execute(null);
        else if (action == "close") vm.Dispose();
        else { vm.ResetCommand.Execute(null); vm.SaveCommand.Execute(null); }
        Unchanged(save, before); Assert.False(save.State.Edited);
        if (action != "reset") Assert.False(vm.SaveCommand.CanExecute(null));
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task CopyToOthers_IsConfirmed_AndSeedScopeIsExplicit(bool includeSeed, bool confirm)
    {
        var save = SvRaidDataSessionTests.CreateSave();
        var data = save.RaidPaldea;
        data.GetRaid(0).AreaID = 1; data.GetRaid(1).AreaID = 2;
        data.GetRaid(1).LotteryGroup = 55; data.GetRaid(1).Seed = 0x11111111;
        data.GetRaid(2).AreaID = 0; data.GetRaid(2).Seed = 0x22222222;
        var before = Snapshot(save);
        var dialogs = new RecordingDialogService { ConfirmResult = confirm };
        using var vm = new Raid9EditorViewModel(save, dialogs: dialogs);
        var row = vm.SelectedRaid!;
        row.Seed = 0xABCDEF01; row.Content = 3; row.IsEnabled = true; row.LeaguePointsClaimed = true;
        vm.IncludeSeed = includeSeed;
        await vm.CopyToOthersCommand.ExecuteAsync(null);
        Assert.Single(dialogs.Confirmations);
        var target = vm.Regions[0].Raids[1];
        Assert.Equal(confirm && includeSeed ? 0xABCDEF01u : 0x11111111u, target.Seed);
        Assert.Equal(confirm ? 3 : 0, target.Content); Assert.Equal(confirm, target.IsEnabled);
        Assert.Equal(confirm, target.LeaguePointsClaimed);
        Assert.Equal(2u, target.Area); Assert.Equal(55u, target.LotteryGroup);
        Assert.Equal(0x22222222u, vm.Regions[0].Raids[2].Seed);
        Unchanged(save, before);
    }
    [Theory]
    [InlineData("")] [InlineData("1")] [InlineData("123456789")] [InlineData("GGGGGGGG")]
    public void InvalidHiddenRaidSeed_BlocksSaveWithoutMutatingSource(string text)
    {
        var save = SvRaidDataSessionTests.CreateSave(); var before = Snapshot(save);
        using var vm = new Raid9EditorViewModel(save);
        vm.SelectedRaid!.SeedHex = text;
        Assert.NotEmpty(vm.SelectedRaid.SeedError);
        vm.SelectedRegion = vm.Regions[2]; vm.SelectedTab = 1;
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null); Unchanged(save, before);
        vm.Regions[0].Raids[0].SeedHex = "FFFFFFFF";
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.Regions[0].CurrentSeedHex = "123";
        Assert.NotEmpty(vm.Regions[0].SeedError); Assert.False(vm.CanSave);
        vm.Regions[0].CurrentSeedHex = "0123456789ABCDEF";
        vm.Regions[0].TomorrowSeedHex = "FEDCBA9876543210";
        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);
        Assert.Equal(0xFFFFFFFFu, save.RaidPaldea.GetRaid(0).Seed);
        Assert.Equal(0x0123456789ABCDEFUL, save.RaidPaldea.CurrentSeed);
        Assert.Equal(0xFEDCBA9876543210UL, save.RaidPaldea.TomorrowSeed);
    }
    [AvaloniaTheory]
    [InlineData("en", 780, 650)] [InlineData("de", 480, 360)]
    public void RealBindings_ValidateSeeds_RestoreRegionSelection_AndKeepActionsVisible(string language, int width, int height)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage; Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var save = SvRaidDataSessionTests.CreateSave(); var before = Snapshot(save);
            save.RaidPaldea.GetRaid(0).Content = (TeraRaidContentType)uint.MaxValue;
            before = Snapshot(save);
            using var vm = new Raid9EditorViewModel(save);
            var view = new Raid9Editor { DataContext = vm };
            window = new Window { Content = view, Width = width, Height = height };
            window.Show(); Pump(window);
            Assert.True(view.FindControl<ListBox>("RaidList")!.Bounds.Height > 50, $"Raid list height: {view.FindControl<ListBox>("RaidList")!.Bounds.Height}");
            Assert.True(view.FindControl<ScrollViewer>("RaidDetails")!.Bounds.Height > 50);
            var input = view.FindControl<TextBox>("RaidSeedInput")!;
            input.Text = "bad"; Pump(window);
            Assert.NotEmpty(vm.SelectedRaid!.SeedError); Assert.False(view.FindControl<Button>("SaveButton")!.IsEffectivelyEnabled);
            input.Text = "12345678"; Pump(window);
            Assert.Equal(0x12345678u, vm.SelectedRaid.Seed);
            view.FindControl<ComboBox>("RegionSelector")!.SelectedItem = vm.Regions[2]; Pump(window);
            Assert.Same(vm.Regions[2].Raids[0], vm.SelectedRaid);
            Assert.Equal(80, view.FindControl<ListBox>("RaidList")!.ItemCount);
            view.FindControl<TabControl>("RaidTabs")!.SelectedIndex = 1; Pump(window);
            view.FindControl<NumericUpDown>("RecordIdentifier")!.Value = 20260930; Pump(window);
            Assert.Equal(20260930u, vm.SelectedRecord!.Identifier);
            foreach (var name in new[] { "SaveButton", "CancelButton" })
            {
                var button = view.FindControl<Button>(name)!; var point = button.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= height);
            }
            Assert.Equal(-1, vm.Regions[0].Raids[0].Content); Unchanged(save, before);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }

    [Fact]
    public void EmptySearchRetainsStagedEdits_AndReportsHiddenInvalidRegion()
    {
        var save = SvRaidDataSessionTests.CreateSave();
        using var vm = new Raid9EditorViewModel(save);
        vm.SelectedRaid!.Seed = 12345;
        vm.SearchText = "missing-raid";
        Assert.Empty(vm.Raids9); Assert.Null(vm.SelectedRaid); Assert.False(vm.HasVisibleRaids);
        Assert.Equal(12345u, vm.Regions[0].Raids[0].Seed);
        vm.Regions[0].Raids[0].SeedHex = "invalid";
        Assert.True(vm.HasValidationError);
        Assert.Contains(vm.Regions[0].Name, vm.ValidationSummary);
        vm.ResetCommand.Execute(null);
        Assert.False(vm.HasValidationError);
        vm.SearchText = string.Empty;
        Assert.Equal(0u, vm.SelectedRaid!.Seed);
    }

    [AvaloniaFact]
    public void RealizedNoOp_AllRegionsAndRecords_PreserveUnknownValuesAndSaveBytes()
    {
        var save = SvRaidDataSessionTests.CreateSave();
        foreach (var data in new[] { save.RaidPaldea, save.RaidKitakami, save.RaidBlueberry })
        {
            var row = data.GetRaid(0);
            row.AreaID = uint.MaxValue; row.LotteryGroup = uint.MaxValue; row.SpawnPointID = uint.MaxValue;
            row.Seed = uint.MaxValue; row.Content = (TeraRaidContentType)uint.MaxValue;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(save.RaidPaldea.Data[16..], 2);
        save.RaidSevenStar.GetRaid(0).Identifier = uint.MaxValue;
        save.RaidSevenStar.Captured.Data[4] = 2;
        var before = Snapshot(save);
        using var vm = new Raid9EditorViewModel(save);
        var view = new Raid9Editor { DataContext = vm };
        var window = new Window { Content = view, Width = 780, Height = 650 };
        window.Show();
        try
        {
            Pump(window);
            foreach (var region in vm.Regions)
            {
                view.FindControl<ComboBox>("RegionSelector")!.SelectedItem = region;
                Pump(window);
                view.FindControl<ScrollViewer>("RaidDetails")!.ScrollToEnd(); Pump(window);
                Assert.Equal(-1, (int)view.FindControl<ComboBox>("ContentSelector")!.SelectedValue!);
                Assert.Equal(uint.MaxValue, vm.SelectedRaid!.Area);
                Assert.Equal(uint.MaxValue, vm.SelectedRaid.Seed);
            }
            view.FindControl<TabControl>("RaidTabs")!.SelectedIndex = 1; Pump(window);
            Assert.Equal((decimal)uint.MaxValue, view.FindControl<NumericUpDown>("RecordIdentifier")!.Value);
            vm.SaveCommand.Execute(null);
            Unchanged(save, before); Assert.False(save.State.Edited);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BothMenuCommands_OpenUnifiedWorkspace_OnTheCorrectTab()
    {
        using var app = new HeadlessAppFixture();
        var directory = SaveFileFixture.FindSaveFilesPath() ?? throw new InvalidOperationException("Save fixture directory missing.");
        var save = Assert.IsType<SAV9SV>(SaveFileFixture.LoadSave(Path.Combine(directory, "gen9_scarlet.main")));
        app.LoadSaveInstance(save);
        await app.ViewModel.OpenRaid9Command.ExecuteAsync(null);
        var first = Assert.IsType<Raid9EditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.Equal(0, first.SelectedTab);
        Assert.True(first.CopyToOthersCommand.CanExecute(null));
        Assert.IsType<Raid9Editor>(global::PKHeX.Avalonia.ViewLocator.Build(first));
        first.Dispose();
        await app.ViewModel.OpenRaidSevenStar9Command.ExecuteAsync(null);
        var second = Assert.IsType<Raid9EditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.Equal(1, second.SelectedTab); Assert.NotEmpty(second.RaidItems);
        Assert.False(second.CopyToOthersCommand.CanExecute(null));
        second.Dispose();
    }
}
