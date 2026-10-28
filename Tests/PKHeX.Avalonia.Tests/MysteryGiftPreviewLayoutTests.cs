using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using PKHeX.Presentation.Localization;
using Avalonia.VisualTree;

namespace PKHeX.Avalonia.Tests;

public class MysteryGiftPreviewLayoutTests
{
    internal static void InitializeGen5Album(SaveFile save)
    {
        if (save is not SAV5 gen5) return;
        // BlankSaveFile uses zero encrypted bytes; initialize a valid empty encrypted album.
        var storage = ((IMysteryGiftStorageProvider)save).MysteryGiftStorage;
        for (int i = 0; i < storage.GiftCountMax; i++) storage.SetMysteryGift(i, new PGF());
        gen5.Mystery.ClearReceivedFlags();
        gen5.Mystery.EndAccess();
    }

    public static IEnumerable<object[]> Cases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { GameVersion.W2, GameVersion.X, GameVersion.SN, GameVersion.GP }.Select(version => new object[] { version, language }));

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void AlbumPreviewRendersWithoutMutatingSave(GameVersion version, string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture();
        LocalizedStrings.Instance.SetLanguage(language);
        var save = BlankSaveFile.Get(version);
        InitializeGen5Album(save);
        var storage = ((IMysteryGiftStorageProvider)save).MysteryGiftStorage;
        var gift = storage.GetMysteryGift(0);
        gift.IsEntity = true;
        gift.Species = 25;
        gift.CardID = 123;
        storage.SetMysteryGift(0, gift);
        if (save is SAV5 gen5) gen5.Mystery.EndAccess();
        save.State.Edited = false;
        var before = save.Data.ToArray();
        app.LoadSaveInstance(save);
        Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);
        var vm = Assert.IsType<MysteryGiftEditorViewModel>(app.ViewModel.MysteryGiftEditor);
        var view = new MysteryGiftEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 700, Height = 420 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(vm.SelectedGift!.PreviewRows);
            Assert.NotNull(vm.SelectedGift.Sprite);
            Assert.True(view.FindControl<ScrollViewer>("CardPreviewScroll")!.Bounds.Height > 70);
            var import = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ImportGiftCommand));
            var point = import.TranslatePoint(default, view)!.Value;
            Assert.True(point.Y >= 0 && point.Y + import.Bounds.Height <= view.Bounds.Height);
            Assert.True(point.X >= 0 && point.X + import.Bounds.Width <= view.Bounds.Width);
            Assert.Equal(before, save.Data.ToArray());
            Assert.False(save.State.Edited);
        }
        finally { window.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
}
