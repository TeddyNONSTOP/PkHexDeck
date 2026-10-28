using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Infrastructure.AutoLegality;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// The Organizer's "Legalize marked": <see cref="BulkSlotActionsController.LegalizeMarkedAsync"/> with a
/// scripted <see cref="IAutoLegalityService"/>, covering the write/undo pipeline, failures that stay
/// marked, an edit made while the engine runs, cancel and protected slots. The real engine is covered
/// separately in <see cref="OrganizerLegalizeEngineTests"/>.
/// </summary>
public sealed class OrganizerLegalizeTests
{
    private readonly List<string> _messages = [];
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly Mock<ISaveBackupService> _backups = new();
    private bool _confirm = true;

    public OrganizerLegalizeTests()
    {
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => _confirm);
        _dialogs.Setup(d => d.ShowInformationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => _messages.Add(message))
            .Returns(Task.CompletedTask);
        _dialogs.Setup(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => _messages.Add("ERROR: " + message))
            .Returns(Task.CompletedTask);
    }

    internal static string F(string key, params object[] args) => LocalizedStrings.Instance.Format(key, args);

    private (BulkSlotActionsController Controller, BoxSelectionService Selection, UndoRedoService Undo) Create(SaveFile sav, IAutoLegalityService? service)
    {
        var undo = new UndoRedoService();
        undo.Initialize(sav);
        var selection = new BoxSelectionService();
        var controller = new BulkSlotActionsController(
            sav, undo, selection, _dialogs.Object, _backups.Object, new AppSettings(), () => "/tmp/organizer-legalize-test.sav",
            autoLegality: service);
        return (controller, selection, undo);
    }

    /// <summary>"Legalizes" by renaming, so the test can see which slots were written.</summary>
    private static Mock<IAutoLegalityService> Scripted(Func<PKM, AutoLegalityResult> legalize)
    {
        var service = new Mock<IAutoLegalityService>();
        service.Setup(s => s.TryLegalizeExisting(It.IsAny<SaveFile>(), It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()))
            .Returns((SaveFile _, PKM pk, LegalityAnalysis? _) => legalize(pk));
        return service;
    }

    private static AutoLegalityResult Fixed(PKM pk)
    {
        var copy = pk.Clone();
        copy.Nickname = "FIXED";
        copy.IsNicknamed = true;
        return AutoLegalityResult.Ok(copy, "ok");
    }

    [Fact]
    public async Task WritesEverySuccess_InOneUndo_AndKeepsFailuresMarked()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 133 }, 0, 1);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 4, Nickname = "Stubborn", IsNicknamed = true }, 0, 2);
        var service = Scripted(pk => pk.Species == 4
            ? AutoLegalityResult.Failure(LegalizationStatus.Failed, null, "no encounter")
            : Fixed(pk));
        var (controller, selection, undo) = Create(sav, service.Object);
        var completed = 0;
        controller.Completed += () => completed++;
        foreach (var slot in new[] { 0, 1, 2 })
            selection.Toggle(SlotLocation.FromBox(0, slot));

        await controller.LegalizeMarkedAsync();

        Assert.Equal("FIXED", sav.GetBoxSlotAtIndex(0, 0).Nickname);
        Assert.Equal("FIXED", sav.GetBoxSlotAtIndex(0, 1).Nickname);
        Assert.Equal("Stubborn", sav.GetBoxSlotAtIndex(0, 2).Nickname);
        Assert.True(sav.State.Edited);
        Assert.Equal(1, completed);
        Assert.Equal([SlotLocation.FromBox(0, 2)], selection.Snapshot()); // only the failure stays marked
        _backups.Verify(b => b.CreateBackup(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()), Times.Once);

        var summary = Assert.Single(_messages);
        Assert.StartsWith(F("Organizer_ResultLegalized", 2), summary);
        Assert.Contains(F("Organizer_LegalizeFailedList", 1, "Stubborn"), summary);

        undo.Undo();
        Assert.NotEqual("FIXED", sav.GetBoxSlotAtIndex(0, 0).Nickname);
        Assert.NotEqual("FIXED", sav.GetBoxSlotAtIndex(0, 1).Nickname);
    }

    [Fact]
    public async Task ASlotEditedWhileTheEngineRuns_IsNotOverwritten()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK6 { Species = 133 }, 0, 1);
        var service = Scripted(pk =>
        {
            if (pk.Species == 25)
                sav.SetBoxSlotAtIndex(new PK6 { Species = 26 }, 0, 0); // the user edits the slot meanwhile
            return Fixed(pk);
        });
        var (controller, selection, _) = Create(sav, service.Object);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 1));

        await controller.LegalizeMarkedAsync();

        Assert.Equal(26, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal("FIXED", sav.GetBoxSlotAtIndex(0, 1).Nickname);
        Assert.StartsWith(F("Organizer_ResultLegalized", 1), Assert.Single(_messages));
    }

    [Fact]
    public async Task Cancel_NeverRunsTheEngine()
    {
        var sav = new SAV6XY();
        sav.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        var service = Scripted(Fixed);
        var (controller, selection, _) = Create(sav, service.Object);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        _confirm = false;

        await controller.LegalizeMarkedAsync();

        service.Verify(s => s.TryLegalizeExisting(It.IsAny<SaveFile>(), It.IsAny<PKM>(), It.IsAny<LegalityAnalysis?>()), Times.Never);
        Assert.Equal(1, selection.Count);
        Assert.False(sav.State.Edited);
    }

    [Fact]
    public async Task ProtectedSlots_AreSkippedAndReported()
    {
        var sav = new SAV7SM();
        sav.BoxLayout.ClearBattleTeams();
        sav.BoxLayout.TeamSlots[0] = 0;
        sav.BoxLayout.SetIsTeamLocked(0, true);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 1 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK7 { Species = 4 }, 0, 5);
        var seen = new List<ushort>();
        var service = Scripted(pk => { seen.Add(pk.Species); return Fixed(pk); });
        var (controller, selection, _) = Create(sav, service.Object);
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 5));

        await controller.LegalizeMarkedAsync();

        Assert.Equal([(ushort)4], seen);
        Assert.NotEqual("FIXED", sav.GetBoxSlotAtIndex(0, 0).Nickname);
        Assert.Contains(F("Organizer_SkippedUnavailable", 1), Assert.Single(_messages));
    }

    [Fact]
    public void Viewers_OfferTheActionOnlyWithAutoLegalityMod()
    {
        var sav = new SAV6XY();
        var (withAlm, selection, _) = Create(sav, Scripted(Fixed).Object);
        var (withoutAlm, _, _) = Create(sav, null);

        Assert.True(new BoxViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withAlm).CanBulkLegalize);
        Assert.True(new PartyViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withAlm).CanBulkLegalize);
        Assert.False(new BoxViewerViewModel(sav, Mock.Of<ISpriteRenderer>(), selection: selection, bulkActions: withoutAlm).CanBulkLegalize);
    }
}

/// <summary>
/// "Legalize marked" against the real vendored Auto Legality Mod engine. Shares the AutoLegality
/// collection with the other engine tests, which run serially because the engine has process-wide
/// static settings.
/// </summary>
[Collection("AutoLegality")]
public sealed class OrganizerLegalizeEngineTests
{
    private readonly IAutoLegalityService _service = new AutoLegalityService();

    public OrganizerLegalizeEngineTests()
    {
        GameInfo.CurrentLanguage = "en";
        GameInfo.Strings = GameInfo.GetStrings("en");
    }

    [Fact]
    public async Task FixesTheIllegalMark_AndLeavesTheLegalOneByteForByte()
    {
        var sav = BlankSaveFile.Get(GameVersion.D);
        var baseline = _service.TryLegalizeShowdownSet(sav, "Bidoof\nLevel: 5");
        Assert.True(baseline.Success, baseline.MessageText);
        var legal = baseline.Pokemon!;
        var illegal = legal.Clone();
        illegal.MetLevel = 100;
        Assert.True(new LegalityAnalysis(legal).Valid);
        Assert.False(new LegalityAnalysis(illegal).Valid);
        sav.SetBoxSlotAtIndex(legal, 0, 0);
        sav.SetBoxSlotAtIndex(illegal, 0, 1);
        var legalBefore = PKHeX.Application.Services.BankEntryInfoFactory.GetRawData(sav.GetBoxSlotAtIndex(0, 0));

        var undo = new UndoRedoService();
        undo.Initialize(sav);
        var selection = new BoxSelectionService();
        selection.Toggle(SlotLocation.FromBox(0, 0));
        selection.Toggle(SlotLocation.FromBox(0, 1));
        var messages = new List<string>();
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        dialogs.Setup(d => d.ShowInformationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string message) => messages.Add(message))
            .Returns(Task.CompletedTask);
        var controller = new BulkSlotActionsController(sav, undo, selection, dialogs.Object, null, null, () => null, autoLegality: _service);

        await controller.LegalizeMarkedAsync();

        Assert.True(new LegalityAnalysis(sav.GetBoxSlotAtIndex(0, 1)).Valid);
        Assert.Equal(legalBefore, PKHeX.Application.Services.BankEntryInfoFactory.GetRawData(sav.GetBoxSlotAtIndex(0, 0)));
        Assert.Equal(0, selection.Count);
        Assert.Equal(
            OrganizerLegalizeTests.F("Organizer_ResultLegalized", 1) + " " + OrganizerLegalizeTests.F("Organizer_SkippedAlreadyLegal", 1),
            Assert.Single(messages));
    }
}
