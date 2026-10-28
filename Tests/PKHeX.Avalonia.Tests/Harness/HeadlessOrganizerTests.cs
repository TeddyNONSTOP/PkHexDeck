using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using Xunit;
using Xunit.Abstractions;

namespace PKHeX.Avalonia.Tests.Harness;

/// <summary>
/// Full-app headless coverage for the Organizer UI: the selection-mode toggle in the composed
/// BoxViewer, click routing (a click marks instead of only selecting), the visible mark overlay on
/// marked box and party-strip slots, the bulk-actions menu, and one bulk release executed from it.
/// Opt-in frame capture via <c>PKHEX_HEADLESS_CAPTURE=1</c> (see Harness/README.md).
/// </summary>
public sealed class HeadlessOrganizerTests(ITestOutputHelper output)
{
    private static SaveFile CreateSave()
    {
        var sav = HeadlessSaveFixtures.CreatePopulatedEmeraldSave((ushort)Species.Bulbasaur);
        var template = sav.GetBoxSlotAtIndex(0, 0);
        foreach (var (slot, species) in new[] { (1, Species.Charmander), (2, Species.Squirtle), (7, Species.Pikachu) })
        {
            var pk = template.Clone();
            pk.Species = (ushort)species;
            sav.SetBoxSlotAtIndex(pk, 0, slot);
        }
        sav.SetPartySlotAtIndex(template.Clone(), 0);
        return sav;
    }

    private static Border? MarkBadge(Button slotButton) =>
        slotButton.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("organizer-mark"));

    [AvaloniaFact]
    public void SelectionMode_MarksSlots_ShowsOverlayAndActionsMenu_AndReleasesFromTheMenu()
    {
        using var app = new HeadlessAppFixture();
        app.Window.Width = 1024;
        app.Window.Height = 720;
        app.LoadSaveInstance(CreateSave());
        var box = app.BoxViewer!;

        var toggle = app.FindByName<ToggleButton>("OrganizerToggle");
        var actions = app.FindByName<Button>("OrganizerActionsButton");
        Assert.NotNull(toggle);
        Assert.NotNull(actions);
        Assert.True(toggle!.IsEffectivelyVisible);
        Assert.False(actions!.IsEffectivelyVisible); // only in selection mode

        app.Click(toggle);
        Assert.True(box.IsSelectionModeEnabled);
        Assert.True(toggle.IsChecked);
        app.PumpUntil(() => actions.IsEffectivelyVisible, because: "the Actions button to appear in selection mode");

        // In selection mode a plain click marks the slot instead of only moving the cursor.
        app.ClickSlot(box: 0, slot: 1);
        app.ClickSlot(box: 0, slot: 2);
        Assert.Equal(2, box.MarkedCount);

        var marked = app.FindSlotButton(0, 1)!;
        var unmarked = app.FindSlotButton(0, 0)!;
        Assert.Contains("marked", marked.Classes);
        Assert.DoesNotContain("marked", unmarked.Classes);
        Assert.True(MarkBadge(marked)!.IsEffectivelyVisible, "Marked slot shows the check badge.");
        Assert.False(MarkBadge(unmarked)!.IsEffectivelyVisible, "Unmarked slot hides the check badge.");
        Assert.Contains(LocalizedStrings.Instance["Organizer_MarkedOverlay"], ((SlotData)marked.Tag!).AccessibleName);

        var count = app.FindByName<TextBlock>("OrganizerMarkedCount");
        Assert.NotNull(count);
        Assert.True(count!.IsEffectivelyVisible);
        Assert.Equal(LocalizedStrings.Instance.Format("Organizer_MarkedCount", 2), count.Text);

        // The party strip shares the party view-model, so a click there marks the party member and
        // the strip slot shows the same overlay.
        var party = app.ViewModel.PartyViewer!;
        var strip = app.Find<PartyStrip>();
        Assert.NotNull(strip);
        var stripSlot = strip!.GetVisualDescendants().OfType<Button>().First(b => b.Tag is PartySlotData { Slot: 0 });
        app.Click(stripSlot);
        Assert.True(party.Slots[0].IsMarked);
        Assert.True(MarkBadge(stripSlot)!.IsEffectivelyVisible);
        Assert.Equal(3, box.MarkedCount);

        // The actions menu lists every bulk verb, enabled because marks exist.
        var flyout = Assert.IsType<MenuFlyout>(actions.Flyout);
        var items = flyout.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header!);
        foreach (var key in new[] { "Organizer_SelectAllInBox", "Organizer_ClearMarks", "Organizer_MoveHere", "Organizer_CopyHere",
                                    "Organizer_MoveToParty", "Organizer_CopyToParty", "Organizer_Duplicate", "Organizer_Export",
                                    "Organizer_ReleaseMarked" })
        {
            Assert.True(items.ContainsKey(LocalizedStrings.Instance[key]), $"Actions menu is missing {key}.");
        }
        flyout.ShowAt(actions);
        app.Pump();
        Assert.True(flyout.IsOpen);

        CaptureIfEnabled(app, "organizer-marked-actions.png");

        var release = items[LocalizedStrings.Instance["Organizer_ReleaseMarked"]];
        var releaseCommand = release.Command;
        Assert.NotNull(releaseCommand);
        Assert.True(releaseCommand!.CanExecute(null));
        flyout.Hide();
        app.Pump();

        app.Dialogs.ConfirmResult = true;
        releaseCommand.Execute(null);
        app.PumpUntil(() => app.Dialogs.Infos.Count > 0, because: "the bulk release to report");

        // Box marks released in one transaction; the party mark was excluded and kept.
        Assert.Single(app.Dialogs.Confirmations);
        Assert.Equal(0, app.Save!.GetBoxSlotAtIndex(0, 1).Species);
        Assert.Equal(0, app.Save.GetBoxSlotAtIndex(0, 2).Species);
        Assert.Equal((ushort)Species.Bulbasaur, app.Save.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(1, app.Save.PartyCount);
        Assert.Equal(1, box.MarkedCount);
        Assert.True(app.ViewModel.UndoCommand.CanExecute(null));
        app.PumpUntil(() => app.FindSlotButton(0, 1) is { Tag: SlotData { IsEmpty: true } }, because: "the box to refresh after release");
        Assert.False(MarkBadge(app.FindSlotButton(0, 1)!)!.IsEffectivelyVisible);

        // Leaving selection mode drops every mark and hides the overlay and menu.
        app.Click(app.FindByName<ToggleButton>("OrganizerToggle")!);
        Assert.False(box.IsSelectionModeEnabled);
        Assert.Equal(0, box.MarkedCount);
        // The party refreshed after the release, so re-resolve the realized strip slot.
        var refreshedStripSlot = strip.GetVisualDescendants().OfType<Button>().First(b => b.Tag is PartySlotData { Slot: 0 });
        Assert.False(party.Slots[0].IsMarked);
        Assert.False(MarkBadge(refreshedStripSlot)!.IsEffectivelyVisible);
        Assert.False(app.FindByName<Button>("OrganizerActionsButton")!.IsEffectivelyVisible);
        output.WriteLine($"Organizer flow OK; dialogs: {string.Join(" | ", app.Dialogs.Infos.Select(i => i.Message))}");
    }

    private void CaptureIfEnabled(HeadlessAppFixture app, string fileName)
    {
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE") != "1")
            return;
        var dir = Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR")
                  ?? Path.Combine(Path.GetTempPath(), "pkhex-headless-frames");
        var saved = app.CaptureFrame(Path.Combine(dir, fileName));
        output.WriteLine(saved is null ? "No frame captured." : $"Saved frame to {saved}");
    }
}
