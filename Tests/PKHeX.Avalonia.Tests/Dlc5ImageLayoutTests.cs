using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Dlc5ImageLayoutTests
{
    public static IEnumerable<object[]> Cases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { "gen5_black.sav", "gen5_white2.sav" }.Select(file => new object[] { language, file }));
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    internal static void Seed(SAV5 save)
    {
        var gear = SkinImage5.WriteCGear(SkinImage5Tests.Solid(Color15Bit.GetColorExpand(31)), save.CGearSkinData.ToArray(), save is SAV5BW);
        save.SetCGearSkin(gear.Data!);
        var dex = new byte[PokeDexSkin5.SIZE];
        for (var tile = 0; tile < 768; tile++)
            for (var p = 0; p < 64; p += 2)
                if ((tile / 32 + tile % 32) % 4 == 0) dex[tile * 32 + p / 2] = 0x11;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dex.AsSpan(0x6002), 31);
        for (var i = 0; i < 64; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dex.AsSpan(0x6020 + i * 2), (ushort)(i * 100));
        save.SetPokeDexSkin(dex); save.State.Edited = false;
    }
    [AvaloniaTheory] [MemberData(nameof(Cases))]
    public void AllLocalesAndBothGames_ShowPreviewsAndReachAllActionsWithoutMutation(string language, string file)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage; Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var path = Path.Combine(SaveFileFixture.FindSaveFilesPath()!, file);
            var save = Assert.IsAssignableFrom<SAV5>(SaveFileFixture.LoadSave(path)); Seed(save); var before = save.Data.ToArray();
            var vm = new DLC5EditorViewModel(save, new RecordingDialogService(), new PngImageCodec());
            var view = new DLC5Editor { DataContext = vm }; window = new Window { Content = view, Width = 620, Height = 420 }; window.Show(); Pump(window);
            var tabs = view.FindControl<TabControl>("DlcTabs")!;
            Assert.NotNull(view.FindControl<Image>("CGearPreview")!.Source);
            tabs.SelectedIndex = 2; Pump(window);
            Assert.NotNull(view.FindControl<Image>("DexForegroundPreview")!.Source);
            Assert.NotNull(view.FindControl<Image>("DexBackgroundPreview")!.Source);
            Assert.NotNull(view.FindControl<Image>("DexCompositePreview")!.Source);
            var binaryExport = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.ExportPokedexSkinCommand));
            var skinScroll = binaryExport.GetVisualAncestors().OfType<ScrollViewer>().First();
            skinScroll.ScrollToEnd(); Pump(window);
            var exportPoint = binaryExport.TranslatePoint(default, skinScroll)!.Value;
            Assert.True(exportPoint.X >= 0 && exportPoint.X + binaryExport.Bounds.Width <= skinScroll.Bounds.Width);
            Assert.True(exportPoint.Y >= 0 && exportPoint.Y + binaryExport.Bounds.Height <= skinScroll.Bounds.Height);
            tabs.SelectedIndex = 5; Pump(window);
            Assert.False(view.FindControl<Button>("ExportDecryptedButton")!.IsEffectivelyEnabled);
            Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [AvaloniaTheory] [InlineData("gen5_black.sav")] [InlineData("gen5_white2.sav")]
    public async Task RealMenuComposition_SuppliesPngCodecAndCompleteMenuLabel(string file)
    {
        using var app = new HeadlessAppFixture();
        var save = Assert.IsAssignableFrom<SAV5>(SaveFileFixture.LoadSave(Path.Combine(SaveFileFixture.FindSaveFilesPath()!, file)));
        app.LoadSaveInstance(save); Seed(save);
        await app.ViewModel.OpenDLC5Command.ExecuteAsync(null);
        var vm = Assert.IsType<DLC5EditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.True(vm.CanLoadImage); Assert.NotNull(vm.CGearPng);
        Assert.IsType<PngImageCodec>(app.Services.GetRequiredService<IImageCodec>());
        Assert.DoesNotContain("C-Gear/PWT/Musical", LocalizedStrings.Instance["Menu_Gen5_DLC"]);
    }
}
