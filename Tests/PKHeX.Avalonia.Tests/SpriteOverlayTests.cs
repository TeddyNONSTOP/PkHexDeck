using Moq;
using PKHeX.Application.Models;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;
using SkiaSharp;

namespace PKHeX.Avalonia.Tests;

public class SpriteOverlayTests
{
    [Fact]
    public void EditorAlphaToggleRefreshesIntrinsicMarkerAndAccessibleState()
    {
        var save = SaveFileFixture.LoadSave(Path.Combine(SaveFileFixture.FindSaveFilesPath()!, "gen9a_legendsza.main"))!;
        var pk = save.GetBoxData(0).First(p => p is IAlphaReadOnly { IsAlpha: true });
        var renderer = new AvaloniaSpriteRenderer(new AppSettings()); renderer.Initialize(save);
        var editor = new PokemonEditorViewModel(pk, save, renderer, Mock.Of<IDialogService>(), Mock.Of<IWindowService>());
        var marked = editor.Sprite;
        Assert.Contains(LocalizedStrings.Instance["SlotState_Alpha"], editor.SpriteAccessibleName);
        editor.IsAlpha = false;
        Assert.DoesNotContain(LocalizedStrings.Instance["SlotState_Alpha"], editor.SpriteAccessibleName);
        Assert.NotEqual(marked, editor.Sprite);
        editor.IsAlpha = true;
        Assert.Contains(LocalizedStrings.Instance["SlotState_Alpha"], editor.SpriteAccessibleName);
        Assert.Equal(marked, editor.Sprite);
    }
    [Theory]
    [InlineData("gen6_x.main")]
    [InlineData("gen8_sword_ct_public.main")]
    [InlineData("gen8a_legendsarceus.main")]
    [InlineData("gen9_scarlet.main")]
    [InlineData("gen9a_legendsza.main")]
    public void SaveOwnedStatesAreRenderedAndAnnouncedForEveryOccupiedSlot(string file)
    {
        var save = SaveFileFixture.LoadSave(Path.Combine(SaveFileFixture.FindSaveFilesPath()!, file))!;
        var renderer = new AvaloniaSpriteRenderer(new AppSettings()); renderer.Initialize(save);
        var vm = new BoxViewerViewModel(save, renderer);
        foreach (var slot in vm.Slots.Where(s => !s.IsEmpty))
        {
            var pk = save.GetBoxSlotAtIndex(slot.Box, slot.Slot);
            var state = SpriteSlotState.Create(pk, save, StorageSlotType.Box, save.GetBoxSlotFlags(slot.Box, slot.Slot), false);
            Assert.Equal(renderer.GetSlotSprite(pk, state), slot.Sprite);
            Assert.Equal(SpriteStateDescription.Describe(pk, state), slot.SemanticSummary);
            Assert.Contains(slot.SemanticSummary, slot.AccessibleName);
            if (pk is IAlphaReadOnly { IsAlpha: true }) Assert.Contains(LocalizedStrings.Instance["SlotState_Alpha"], slot.AccessibleName);
        }
    }

    [Fact]
    public void AlphaAndGigantamaxChangeOnlyTheirReservedLanesAndPreserveMegaForms()
    {
        var renderer = new AvaloniaSpriteRenderer(new AppSettings());
        foreach (var pk in new PKM[] { new PA8 { Species = 25 }, new PA9 { Species = 6, Form = 1 }, new PK8 { Species = 6 } })
        {
            using var plain = SKBitmap.Decode(renderer.GetSprite(pk)!);
            var alpha = pk is IAlpha;
            if (pk is IAlpha a) a.IsAlpha = true;
            if (pk is IGigantamax g) g.CanGigantamax = true;
            using var marked = SKBitmap.Decode(renderer.GetSprite(pk)!);
            Assert.Equal((68, 56), (marked.Width, marked.Height));
            var lane = alpha ? SKRectI.Create(48, 0, 20, 20) : SKRectI.Create(24, 0, 20, 20);
            Assert.NotEqual(Pixels(plain, lane), Pixels(marked, lane));
            Assert.Equal(Pixels(plain, SKRectI.Create(0, 20, 68, 36)), Pixels(marked, SKRectI.Create(0, 20, 68, 36)));
        }
        using var megaX = SKBitmap.Decode(renderer.GetSprite(new PA9 { Species = 6, Form = 1, IsAlpha = true })!);
        using var megaY = SKBitmap.Decode(renderer.GetSprite(new PA9 { Species = 6, Form = 2, IsAlpha = true })!);
        Assert.NotEqual(Pixels(megaX, SKRectI.Create(0, 20, 68, 36)), Pixels(megaY, SKRectI.Create(0, 20, 68, 36)));
    }

    [Fact]
    public void ConcurrentSlotStatesUseSeparateLanesAndEmptySlotsRetainOnlyStorageMarkers()
    {
        var renderer = new AvaloniaSpriteRenderer(new AppSettings());
        var pk = new PA9 { Species = 25, IsAlpha = true, HeldItem = 81, IsEgg = true }; pk.SetShiny();
        var flags = StorageSlotSource.BattleTeam1 | StorageSlotSource.Locked | StorageSlotSource.Party2 | StorageSlotSource.Starter;
        var state = new SpriteSlotState(true, false, flags);
        using var plain = SKBitmap.Decode(renderer.GetSprite(pk)!);
        using var marked = SKBitmap.Decode(renderer.GetSlotSprite(pk, state)!);
        var lanes = new[] { SKRectI.Create(0, 44, 12, 12), SKRectI.Create(0, 30, 12, 12), SKRectI.Create(14, 30, 12, 12), SKRectI.Create(14, 44, 12, 12), SKRectI.Create(28, 44, 12, 12) };
        Assert.Equal(Pixels(plain, lanes[0]), Pixels(marked, lanes[0])); // Illegal status belongs to the slot view.
        foreach (var lane in lanes.Skip(1)) Assert.NotEqual(Pixels(plain, lane), Pixels(marked, lane));
        Assert.Equal(Pixels(plain, SKRectI.Create(42, 32, 26, 24)), Pixels(marked, SKRectI.Create(42, 32, 26, 24)));
        var description = SpriteStateDescription.Describe(pk, state);
        foreach (var key in new[] { "Alpha", "Egg", "Shiny", "Illegal", "Team", "Locked", "Starter" }) Assert.Contains(LocalizedStrings.Instance["SlotState_" + key], description);
        Assert.Contains(LocalizedStrings.Instance.Format("SlotState_Party", 2), description);
        Assert.Equal(renderer.GetEmptySlot(), renderer.GetSlotSprite(new PA9(), default));
        using var empty = SKBitmap.Decode(renderer.GetSlotSprite(new PA9(), state)!);
        Assert.All(Pixels(empty, SKRectI.Create(0, 0, 68, 30)), p => Assert.Equal(0, p.Alpha));
        Assert.All(Pixels(empty, lanes[0]), p => Assert.Equal(0, p.Alpha));
        foreach (var lane in lanes.Skip(1)) Assert.Contains(Pixels(empty, lane), p => p.Alpha > 0);
        using var hint = SKBitmap.Decode(renderer.GetSlotSprite(pk, state with { Illegal = false, MoveHint = true })!);
        Assert.NotEqual(Pixels(marked, lanes[0]), Pixels(hint, lanes[0]));
        Assert.Equal(renderer.GetSlotSprite(pk, state), renderer.GetSlotSprite(pk, state with { MoveHint = true }));
    }

    private static SKColor[] Pixels(SKBitmap bitmap, SKRectI rect) =>
        Enumerable.Range(rect.Top, rect.Height).SelectMany(y => Enumerable.Range(rect.Left, rect.Width).Select(x => bitmap.GetPixel(x, y))).ToArray();
}
