using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace PKHeX.Avalonia.Tests;

public sealed class MainWindowFileCommandTests
{
    [AvaloniaFact]
    public async Task CloseModifiedSave_OffersCancelDiscardAndSettingOptOut()
    {
        using var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.SL);
        app.LoadSaveInstance(save);
        save.State.Edited = true;

        await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
        Assert.Same(save, app.Save);
        Assert.Single(app.Dialogs.UnsavedPrompts);

        app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
        app.Pump();
        Assert.Null(app.Save);

        app.LoadSaveInstance(save);
        save.State.Edited = true;
        app.Services.GetRequiredService<AppSettings>().EditorBehavior.WarnClosingModified = false;
        await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
        app.Pump();
        Assert.Null(app.Save);
        Assert.Equal(2, app.Dialogs.UnsavedPrompts.Count);
    }

    [AvaloniaFact]
    public async Task SavingModifiedSave_ClearsFlagAndWritesLatestSlot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkhex-unsaved-{Guid.NewGuid():N}.sav");
        try
        {
            using var app = new HeadlessAppFixture();
            var save = BlankSaveFile.Get(GameVersion.SL);
            app.LoadSaveInstance(save);
            var pokemon = save.BlankPKM;
            pokemon.Species = 25;
            pokemon.RefreshChecksum();
            app.BoxViewer!.SetSlotPKM(0, pokemon);
            Assert.True(save.State.Edited);

            app.Dialogs.SaveFileResult = path;
            app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Save;
            await app.ViewModel.CloseFileCommand.ExecuteAsync(null);
            app.Pump();

            Assert.Null(app.Save);
            Assert.False(save.State.Edited);
            Assert.True(File.Exists(path));
            Assert.NotEmpty(File.ReadAllBytes(path));
            Assert.Single(app.Dialogs.UnsavedPrompts);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task OpeningAnotherSave_RespectsCancelAndDiscard()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../savefiles/gen9_scarlet.main"));
        using var app = new HeadlessAppFixture();
        var original = BlankSaveFile.Get(GameVersion.SL);
        app.LoadSaveInstance(original);
        original.State.Edited = true;
        app.Dialogs.OpenFileResult = path;

        await app.ViewModel.OpenFileCommand.ExecuteAsync(null);
        Assert.Same(original, app.Save);
        Assert.Single(app.Dialogs.UnsavedPrompts);

        app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        await app.ViewModel.OpenFileCommand.ExecuteAsync(null);
        app.Pump();
        Assert.NotSame(original, app.Save);
        Assert.Equal(2, app.Dialogs.UnsavedPrompts.Count);
    }

    [AvaloniaFact]
    public void ClosingWindow_RespectsCancelAndDiscard()
    {
        using var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.SL);
        app.LoadSaveInstance(save);
        save.State.Edited = true;

        app.Window.Close();
        app.Pump();
        Assert.True(app.Window.IsVisible);
        Assert.Single(app.Dialogs.UnsavedPrompts);

        app.Dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        app.Window.Close();
        app.Pump();
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task DumpBoxesCommand_WritesFromTheLoadedSaveAndCompletesOffTheCommandStack()
    {
        var root = Directory.CreateTempSubdirectory("pkhex-dump-command-");
        try
        {
            var save = BlankSaveFile.Get(GameVersion.SL);
            var pokemon = save.BlankPKM;
            pokemon.Species = 25;
            pokemon.RefreshChecksum();
            save.SetBoxSlotAtIndex(pokemon, 0, 0);

            using var app = new HeadlessAppFixture();
            app.LoadSaveInstance(save);
            app.Dialogs.OpenFolderResult = root.FullName;

            await app.ViewModel.DumpBoxesCommand.ExecuteAsync(null);
            app.Pump();

            Assert.False(app.ViewModel.IsBoxTransferRunning);
            Assert.NotEmpty(Directory.GetFiles(root.FullName));
            Assert.Empty(app.Dialogs.Errors);
            Assert.Contains("Dumped", Assert.Single(app.Dialogs.Infos).Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch { /* best effort */ }
        }
    }

    [AvaloniaFact]
    public async Task LoadBoxesCommand_ImportsOnAWorkingCopyThenAppliesOnTheUiThread()
    {
        var root = Directory.CreateTempSubdirectory("pkhex-load-command-");
        try
        {
            var save = BlankSaveFile.Get(GameVersion.SL);
            var source = save.BlankPKM;
            source.Species = 25;
            source.RefreshChecksum();
            File.WriteAllBytes(Path.Combine(root.FullName, "pikachu.pk9"), source.Data.ToArray());

            using var app = new HeadlessAppFixture();
            app.LoadSaveInstance(save);
            app.Dialogs.OpenFolderResult = root.FullName;

            await app.ViewModel.LoadBoxesCommand.ExecuteAsync(null);
            app.Pump();

            Assert.False(app.ViewModel.IsBoxTransferRunning);
            Assert.Equal((ushort)25, save.GetBoxSlotAtIndex(0, 0).Species);
            Assert.True(save.State.Edited);
            Assert.Empty(app.Dialogs.Errors);
            Assert.Contains("Loaded", Assert.Single(app.Dialogs.Infos).Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch { /* best effort */ }
        }
    }

    [AvaloniaFact]
    public async Task SaveFileChangedRaisedOffUiThread_MarshalsEditorBuildToUiThread()
    {
        using var app = new HeadlessAppFixture();
        var save = BlankSaveFile.Get(GameVersion.SL);

        // Native file pickers are allowed to resume their continuation off the UI thread. The
        // production gateway therefore may raise SaveFileChanged from a worker too.
        await Task.Run(() => app.Gateway.OpenLoadedSave(save, "worker.main"));

        app.PumpUntil(
            () => app.ViewModel.HasSave && app.ViewModel.BoxViewer is not null,
            because: "save-change notification to be applied on the Avalonia UI thread");

        Assert.Same(save, app.Save);
        Assert.Empty(app.Dialogs.Errors);
    }
}
