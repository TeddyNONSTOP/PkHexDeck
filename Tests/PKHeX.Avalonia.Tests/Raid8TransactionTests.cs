using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Raid8TransactionTests
{
    public static SAV8SWSH CreateSave(int revision = 2)
    {
        var allocated = new SAV8SWSH { Version = GameVersion.SW };
        var exclude = revision switch
        {
            0 => new uint[] { 0x158DA896, 0x148DA703, 0x3F936BA9, 0x3C9366F0 },
            1 => new uint[] { 0x148DA703, 0x3C9366F0 },
            _ => [],
        };
        var save = new SAV8SWSH(SwishCrypto.Encrypt(allocated.AllBlocks.Where(b => !exclude.Contains(b.Key)).Select(CreateObjectBlock).ToArray()));
        foreach (var raids in new[] { save.RaidGalar, save.RaidArmor, save.RaidCrown })
        {
            if (raids.Data.Length == 0) continue;
            var den = raids.GetRaid(0);
            den.Hash = 0x123456789ABCDEF0;
            den.Seed = 0x1020304050607080;
            den.DenType = RaidType.RareWish;
            den.Flags = 0x80;
            raids.Data[20] = 0xA7;
            raids.GetRaid(1).Activate(3, 70);
        }
        save.State.Edited = false;
        return save;
    }
    // Core's blank allocation intentionally uses None types and cannot be exported/reloaded.
    // Give this synthetic fixture explicit object headers through Core's public parser.
    // This is a fixture encoding step, not a repair of real saves or a production schema claim.
    private static SCBlock CreateObjectBlock(SCBlock block)
    {
        var xor = new SCXorShift32(block.Key);
        var raw = block.Data;
        var encoded = new byte[raw.Length == 0 ? 1 : raw.Length + 5];
        encoded[0] = (byte)((byte)(raw.Length == 0 ? SCTypeCode.Bool1 : SCTypeCode.Object) ^ xor.Next());
        if (raw.Length != 0)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(1), raw.Length ^ xor.Next32());
            for (var index = 0; index < raw.Length; index++) encoded[index + 5] = (byte)(raw[index] ^ xor.Next());
        }
        var offset = 0;
        return SCBlock.ReadFromOffset(encoded, block.Key, ref offset);
    }

    private static Dictionary<uint, byte[]> Snapshot(SAV8SWSH save) => save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
    private static void AssertUnchanged(SAV8SWSH save, Dictionary<uint, byte[]> before)
    {
        foreach (var block in save.AllBlocks) Assert.Equal(before[block.Key], block.Data.ToArray());
    }
    [Theory]
    [InlineData(0, 1)] [InlineData(1, 2)] [InlineData(2, 3)]
    public void Availability_UsesPresentRegionBlocks(int revision, int count)
    {
        var save = CreateSave(revision);
        Assert.Equal(revision, save.SaveRevision);
        var vm = new RaidEditorViewModel(save);
        Assert.Equal(count, vm.Regions.Count);
        Assert.Equal(100, vm.Regions[0].Dens.Count);
        if (count >= 2) Assert.Equal(90, vm.Regions[1].Dens.Count);
        if (count == 3) Assert.Equal(86, vm.Regions[2].Dens.Count);
        Assert.Equal(MaxRaidOrigin.Galar, vm.SelectedRegion!.Origin);
    }
    [Fact]
    public void EveryRegion_StagesFields_Deactivation_CancelAndCloseDiscard()
    {
        var save = CreateSave();
        var before = Snapshot(save);
        var vm = new RaidEditorViewModel(save);
        foreach (var region in vm.Regions)
        {
            vm.SelectedRegion = region;
            var row = vm.SelectedDen!;
            row.SeedHex = "FFEEDDCCBBAA9988";
            row.Stars = 4;
            row.RandRoll = 90;
            row.WattsHarvested = true;
            row.IsEvent = true;
            region.Dens[1].DeactivateCommand.Execute(null);
        }
        vm.SelectedRegion = vm.Regions[0];
        Assert.Equal("FFEEDDCCBBAA9988", vm.SelectedDen!.SeedHex);
        AssertUnchanged(save, before);
        Assert.False(save.State.Edited);
        var closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        AssertUnchanged(save, before);
        // Window close has no commit callback: abandoning the VM leaves the same source intact.
        vm = new RaidEditorViewModel(save);
        vm.SelectedDen!.Stars = 3;
        AssertUnchanged(save, before);
    }
    [Fact]
    public void Save_ChangesAllRegions_AndPreservesHashPaddingOtherBlocksAndConcurrentFlagBits()
    {
        var save = CreateSave();
        var before = Snapshot(save);
        var vm = new RaidEditorViewModel(save);
        foreach (var region in vm.Regions)
        {
            var row = region.Dens[0];
            row.Seed = 0x8877665544332211;
            row.Stars = 4;
            row.RandRoll = 100;
            row.DenType = (int)RaidType.CommonWish;
            row.WattsHarvested = true;
            row.IsEvent = true;
        }
        save.RaidGalar.GetRaid(0).Flags |= 0x40;
        save.Money = 712345;
        var liveBeforeCommit = Snapshot(save);
        vm.SaveCommand.Execute(null);
        Assert.True(save.State.Edited);
        Assert.Equal(712345u, save.Money);
        foreach (var raids in new[] { save.RaidGalar, save.RaidArmor, save.RaidCrown })
        {
            var row = raids.GetRaid(0);
            Assert.Equal(0x8877665544332211UL, row.Seed);
            Assert.Equal((byte)4, row.Stars);
            Assert.Equal((byte)100, row.RandRoll);
            Assert.Equal(RaidType.CommonWish, row.DenType);
            Assert.True(row.WattsHarvested);
            Assert.True(row.IsEvent);
            Assert.Equal(0x123456789ABCDEF0UL, row.Hash);
            Assert.Equal(0xA7, raids.Data[20]);
        }
        Assert.Equal((byte)0xC3, save.RaidGalar.GetRaid(0).Flags);
        var changedKeys = new uint[] { 0x9033eb7b, 0x158DA896, 0x148DA703 };
        foreach (var block in save.AllBlocks)
        {
            if (changedKeys.Contains(block.Key))
                Assert.Equal(before[block.Key].AsSpan(24).ToArray(), block.Data[24..].ToArray());
            else
                Assert.Equal(liveBeforeCommit[block.Key], block.Data.ToArray());
        }
        var reloaded = new SAV8SWSH(save.Write());
        Assert.Equal(save.RaidGalar.Data.ToArray(), reloaded.RaidGalar.Data.ToArray());
        Assert.Equal(save.RaidArmor.Data.ToArray(), reloaded.RaidArmor.Data.ToArray());
        Assert.Equal(save.RaidCrown.Data.ToArray(), reloaded.RaidCrown.Data.ToArray());
    }
    [Fact]
    public void InvalidSeedInInactiveRegion_BlocksSave_AndResetDiscardsAllRegions()
    {
        var save = CreateSave();
        var before = Snapshot(save);
        var vm = new RaidEditorViewModel(save);
        vm.SelectedDen!.SeedHex = "123";
        Assert.NotEmpty(vm.SelectedDen.SeedError);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SelectedRegion = vm.Regions[2];
        vm.SelectedDen!.Stars = 4;
        Assert.False(vm.CanSave);
        vm.SaveCommand.Execute(null);
        AssertUnchanged(save, before);
        vm.ResetCommand.Execute(null);
        Assert.True(vm.CanSave);
        foreach (var region in vm.Regions) Assert.Equal((byte)0, region.Dens[0].Stars);
        vm.SaveCommand.Execute(null);
        AssertUnchanged(save, before);
        Assert.False(save.State.Edited);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Deactivate_CommitsOnlyTheSelectedDen(int region)
    {
        var save = CreateSave();
        var vm = new RaidEditorViewModel(save);
        vm.SelectedRegion = vm.Regions[region];
        vm.SelectedDen = vm.Dens[1];
        vm.SelectedDen.DeactivateCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        var raids = region switch { 1 => save.RaidArmor, 2 => save.RaidCrown, _ => save.RaidGalar };
        Assert.False(raids.GetRaid(1).IsActive);
        Assert.Equal((byte)0, raids.GetRaid(1).Stars);
        Assert.Equal((byte)0, raids.GetRaid(1).RandRoll);
        Assert.True(raids.GetRaid(0).IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NoOpSave_RetainsExistingEditedState(bool edited)
    {
        var save = CreateSave();
        save.State.Edited = edited;
        var before = Snapshot(save);
        var vm = new RaidEditorViewModel(save);
        vm.SaveCommand.Execute(null);
        AssertUnchanged(save, before);
        Assert.Equal(edited, save.State.Edited);
    }

    [AvaloniaFact]
    public void BoundSeedValidationAndRegionSelector_KeepChangesStaged()
    {
        var save = CreateSave();
        var before = Snapshot(save);
        var vm = new RaidEditorViewModel(save);
        var view = new RaidEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 700, Height = 650 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var input = view.FindControl<TextBox>("SeedInput")!;
            input.Text = "invalid";
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(vm.SelectedDen!.SeedError);
            Assert.False(view.FindControl<Button>("SaveButton")!.IsEffectivelyEnabled);
            input.Text = "1234567890ABCDEF";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0x1234567890ABCDEFUL, vm.SelectedDen.Seed);
            Assert.True(view.FindControl<Button>("SaveButton")!.IsEffectivelyEnabled);
            view.FindControl<ComboBox>("RegionSelector")!.SelectedItem = vm.Regions[2];
            Dispatcher.UIThread.RunJobs();
            Assert.Same(vm.Regions[2], vm.SelectedRegion);
            Assert.Same(vm.Regions[2].Dens[0], vm.SelectedDen);
            Assert.Equal(86, view.FindControl<ListBox>("DenList")!.ItemCount);
            Assert.Equal(0x1234567890ABCDEFUL, vm.Regions[0].Dens[0].Seed);
            AssertUnchanged(save, before);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void DerivedDenControls_StaySynchronizedWithRawFields()
    {
        var vm = new RaidEditorViewModel(CreateSave());
        var row = vm.SelectedDen!;
        row.IsEvent = true;
        Assert.Equal((int)RaidType.Event, row.DenType);
        Assert.False(row.IsRare);
        Assert.False(row.IsWishingPiece);
        row.IsRare = true;
        Assert.False(row.IsEvent);
        row.IsWishingPiece = true;
        Assert.Equal((int)RaidType.RareWish, row.DenType);
        row.Flags = 3;
        Assert.True(row.WattsHarvested);
        Assert.True(row.IsEvent);
        row.DeactivateCommand.Execute(null);
        Assert.False(row.IsActive);
        Assert.False(row.WattsHarvested);
        Assert.False(row.IsEvent);
    }

    [AvaloniaTheory]
    [InlineData("en", 700, 650)] [InlineData("de", 480, 360)]
    public void Layout_UnknownValuesSurviveRealization_AndSaveCancelRemainReachable(string language, int width, int height)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var save = CreateSave();
            save.RaidGalar.Data[18] = 254;
            save.RaidGalar.Data[16] = 254;
            var before = Snapshot(save);
            var vm = new RaidEditorViewModel(save);
            var view = new RaidEditor { DataContext = vm };
            window = new Window { Content = view, Width = width, Height = height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(254, vm.SelectedDen!.DenType);
            Assert.Contains(vm.SelectedDen.DenTypes, c => c.Value == 254);
            Assert.Equal((byte)254, vm.SelectedDen.Stars);
            foreach (var name in new[] { "SaveButton", "CancelButton" })
            {
                var button = view.FindControl<Button>(name)!;
                var point = button.TranslatePoint(default, view)!.Value;
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= height);
                Assert.True(button.Bounds.Width > 0);
            }
            Assert.Equal(3, view.FindControl<ComboBox>("RegionSelector")!.ItemCount);
            vm.SaveCommand.Execute(null);
            AssertUnchanged(save, before);
            Assert.False(save.State.Edited);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
