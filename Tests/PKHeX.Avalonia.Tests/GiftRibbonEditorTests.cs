using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;
using Xunit;

namespace PKHeX.Avalonia.Tests;

public sealed class GiftRibbonEditorTests
{
    [Fact]
    public void AllLegalUsesUpstreamGen3DescriptionIndexesOnAStagedCopy()
    {
        var save = new SAV3E();
        var block = (IGiftRibbons)save;
        var before = block.GiftRibbons.ToArray();
        var vm = new GiftRibbonEditorViewModel(save);

        Assert.Equal(11, vm.Ribbons.Count);
        Assert.Equal(3, vm.Generation);
        vm.AllLegalCommand.Execute(null);

        Assert.Equal(32, GetValue(vm, RibbonIndex.Country));
        Assert.Equal(44, GetValue(vm, RibbonIndex.National));
        Assert.Equal(45, GetValue(vm, RibbonIndex.Earth));
        Assert.Equal(32, GetValue(vm, RibbonIndex.World));
        Assert.All(vm.Ribbons.Where(r => r.Ribbon is not (RibbonIndex.Country or RibbonIndex.National or RibbonIndex.Earth or RibbonIndex.World)),
            ribbon => Assert.Equal(0, ribbon.DescriptionIndex));
        Assert.Equal(before, block.GiftRibbons.ToArray());

        vm.SaveCommand.Execute(null);

        Assert.Equal(32, block.GiftRibbons[3]);
        Assert.Equal(44, block.GiftRibbons[4]);
        Assert.Equal(45, block.GiftRibbons[5]);
        Assert.Equal(32, block.GiftRibbons[6]);
        Assert.True(save.State.Edited);
    }

    [Fact]
    public void AllLegalUsesUpstreamGen4DescriptionIndexes()
    {
        var save = new SAV4DP();
        var block = (IGiftRibbons)save;
        var vm = new GiftRibbonEditorViewModel(save);

        Assert.Equal(14, vm.Ribbons.Count);
        Assert.Equal(4, vm.Generation);
        vm.AllLegalCommand.Execute(null);

        Assert.Equal(64, GetValue(vm, RibbonIndex.Classic));
        Assert.Equal(59, GetValue(vm, RibbonIndex.Premier));
        Assert.All(vm.Ribbons.Where(r => r.Ribbon is not (RibbonIndex.Classic or RibbonIndex.Premier)),
            ribbon => Assert.Equal(0, ribbon.DescriptionIndex));
        vm.SaveCommand.Execute(null);

        Assert.Equal(64, block.GiftRibbons[12]);
        Assert.Equal(59, block.GiftRibbons[13]);
    }

    [Fact]
    public void ClearAndCancelNeverChangeTheSourceBlock()
    {
        var save = new SAV3XD();
        var block = (IGiftRibbons)save;
        for (int i = 0; i < block.GiftRibbons.Length; i++)
            block.GiftRibbons[i] = (byte)(i + 1);
        var before = block.GiftRibbons.ToArray();

        var vm = new GiftRibbonEditorViewModel(save);
        vm.ClearCommand.Execute(null);
        Assert.All(vm.Ribbons, ribbon => Assert.Equal(0, ribbon.DescriptionIndex));
        Assert.Equal(before, block.GiftRibbons.ToArray());
        vm.Ribbons[0].DescriptionIndex = 22;
        vm.CancelCommand.Execute(null);

        Assert.Equal(before, block.GiftRibbons.ToArray());
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void AllSupportedSaveTypesPreserveEveryRawDescriptionByteOnSave()
    {
        SaveFile[] saves =
        [
            new SAV3RS(), new SAV3E(), new SAV3FRLG(),
            new SAV3Colosseum(new byte[ColoCrypto.SAVE_SIZE], decrypt: false), new SAV3XD(),
            new SAV4DP(), new SAV4Pt(), new SAV4HGSS(),
        ];

        foreach (var save in saves)
        {
            var block = (IGiftRibbons)save;
            var expected = Enumerable.Range(0, block.GiftRibbons.Length)
                .Select(i => (byte)(i == 0 ? byte.MaxValue : i * 7))
                .ToArray();
            expected.CopyTo(block.GiftRibbons);

            var vm = new GiftRibbonEditorViewModel(save);
            Assert.True(vm.IsSupported);
            Assert.Equal(expected.Length, vm.Ribbons.Count);
            Assert.Equal(expected.Select(value => (int)value), vm.Ribbons.Select(ribbon => ribbon.DescriptionIndex));

            vm.SaveCommand.Execute(null);

            Assert.Equal(expected, block.GiftRibbons.ToArray());
        }
    }

    [Fact]
    public void UnsupportedSavesDoNotExposeAnEditableGiftRibbonBlock()
    {
        var vm = new GiftRibbonEditorViewModel(new SAV5B2W2());

        Assert.False(vm.IsSupported);
        Assert.Empty(vm.Ribbons);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void ViewRendersEveryRibbonWithAnAccessibleDescriptionIndex()
    {
        using var app = new HeadlessAppFixture();
        var vm = new GiftRibbonEditorViewModel(new SAV3E());
        app.Window.Content = new GiftRibbonEditorView { DataContext = vm };
        app.Window.Width = 560;
        app.Window.Height = 520;
        app.Pump();

        var fields = app.Window.GetVisualDescendants()
            .OfType<NumericUpDown>()
            .Where(field => field.IsVisible)
            .ToList();
        Assert.Equal(11, fields.Count);
        Assert.All(fields, field => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field))));
    }

    [AvaloniaFact]
    public void MenuCapabilityAppearsOnlyForGen3AndGen4GiftRibbonSaves()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV3E());
        var capability = app.ViewModel.ToolLauncherItems.Single(item =>
            item.Title == LocalizedStrings.Instance["Menu_Save_GiftRibbons"]);

        Assert.True(capability.IsAvailable);
        Assert.Contains(capability, app.ViewModel.FilteredToolLauncherItems);

        app.LoadSaveInstance(new SAV5B2W2());
        app.Pump();

        Assert.False(capability.IsAvailable);
        Assert.DoesNotContain(capability, app.ViewModel.FilteredToolLauncherItems);
    }

    private static int GetValue(GiftRibbonEditorViewModel vm, RibbonIndex ribbon) =>
        vm.Ribbons.Single(item => item.Ribbon == ribbon).DescriptionIndex;
}
