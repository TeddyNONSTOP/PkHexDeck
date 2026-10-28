using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class JoinAvenueAdvancedLayoutTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    public static IEnumerable<object[]> LayoutCases => LocalizedStrings.SupportedLanguages.Select(language =>
        new object[] { language, language == "en" ? 1020 : 820, language == "en" ? 760 : 640 });
    [AvaloniaTheory]
    [MemberData(nameof(LayoutCases))]
    public void StableSlotListsAndAdvancedFields_AreReachable_AndOpeningPreservesUnknownBytes(string language, int width, int height)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage; Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var directory = SaveFileFixture.FindSaveFilesPath()!;
            var save = Assert.IsType<SAV5B2W2>(SaveFileFixture.LoadSave(Path.Combine(directory, "gen5_white2.sav")));
            var raw = save.JoinAvenue.Self.Write().ToArray(); raw[0xAD] = 255;
            save.JoinAvenue.Self.CopyFrom(new JoinAvenueVisitor5(raw));
            save.JoinAvenue.Self.Gender = 7; save.JoinAvenue.Self.PlayedMinutes = 63;
            save.JoinAvenue.Self.MetMonth = 255; save.JoinAvenue.Self.MetDay = 255;
            save.JoinAvenue.Self.FavoriteSpecies = 1023; save.JoinAvenue.Self.Date1 = new JoinAvenueDate5(ushort.MaxValue);
            var settings = save.JoinAvenue.Data[(0x12F4)..];
            BinaryPrimitives.WriteUInt16LittleEndian(settings[0xD8..], ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[0xDA..], ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[0xE0..], 45);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[0xE2..], 4000);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[0xE8..], 12345);
            save.State.Edited = false; var before = save.Data.ToArray();
            var vm = new JoinAvenueEditorViewModel(save); var view = new JoinAvenueEditor { DataContext = vm };
            window = new Window { Content = view, Width = width, Height = height }; window.Show(); Pump(window);
            var tabs = view.FindControl<TabControl>("AvenueTabs")!;
            foreach (var (tab, name, count) in new[] { (2, "Visitors", 8), (3, "Fans", 12), (4, "Occupants", 8), (5, "Assistants", 4) })
            {
                tabs.SelectedIndex = tab; Pump(window);
                var list = view.FindControl<ListBox>(name + "List")!;
                Assert.Equal(count, list.ItemCount); Assert.True(list.Bounds.Height > 200);
                list.SelectedIndex = count - 1; Pump(window);
                var selected = Assert.IsAssignableFrom<JoinAvenueEntityViewModel>(list.SelectedItem);
                var expander = view.GetVisualDescendants().OfType<Expander>().Single(e => ReferenceEquals(e.DataContext, selected));
                expander.IsExpanded = true; Pump(window);
                var scroll = view.FindControl<ScrollViewer>(name + "Details")!;
                scroll.ScrollToEnd(); Pump(window);
                Assert.True(expander.Bounds.Height > 100);
                var finalInput = expander.GetVisualDescendants().OfType<NumericUpDown>().Last();
                var point = finalInput.TranslatePoint(default, scroll)!.Value;
                Assert.True(point.Y >= 0 && point.Y + finalInput.Bounds.Height <= scroll.Bounds.Height);
                Assert.True(point.X >= 0 && point.X + finalInput.Bounds.Width <= scroll.Bounds.Width);
            }
            tabs.SelectedIndex = 1; Pump(window);
            var selfAdvanced = view.GetVisualDescendants().OfType<Expander>().Single(e => ReferenceEquals(e.DataContext, vm.Self));
            selfAdvanced.IsExpanded = true; Pump(window);
            Assert.True(vm.Self.AdvancedDates[0].HasInvalidStoredDate);
            Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
            vm.Self.AdvancedDates[0].ClearCommand.Execute(null);
            Assert.Equal(0, save.JoinAvenue.Self.Date1.RawValue);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
