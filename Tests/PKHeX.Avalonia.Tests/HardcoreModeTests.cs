using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Hardcore mode's save check (<see cref="HardcoreSaveVerifier"/>, <see cref="HardcoreMode"/>) and its
/// enforcement in <see cref="SaveFileService"/>: moves, releases and box layout pass; edits, new
/// Pokémon, copies and changes outside the boxes are refused.
/// </summary>
public sealed class HardcoreModeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pkhex-hardcore-tests-" + Guid.NewGuid().ToString("N"));

    public HardcoreModeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    public static TheoryData<string> Saves =>
    [
        "gen1_red.sav",
        "gen2_gold.sav",
        "gen3_emerald.sav",
        "gen4_platinum.sav",
        "gen5_white2.sav",
        "gen6_x.main",
        "gen7_ultrasun.main",
        "gen8_sword_ct_public.main",
        "gen8a_legendsarceus.main",
        "gen8b_brilliantdiamond.bin",
        "gen9_scarlet.main",
        "gen9a_legendsza.main",
        "LuminescentPlatinum_test.bin",
    ];

    private static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles", name));

    private static SaveFile Load(string name)
        => FileUtil.GetSupportedFile(FixturePath(name)) as SaveFile
           ?? throw new InvalidOperationException($"{name} is not a save");

    private static (int Box, int Slot)[] Slots(SaveFile sav, bool occupied) =>
    [
        .. Enumerable.Range(0, sav.BoxCount)
            .SelectMany(box => Enumerable.Range(0, sav.BoxSlotCount).Select(slot => (box, slot)))
            .Where(s => !sav.IsBoxSlotOverwriteProtected(s.box, s.slot)
                        && (sav.GetBoxSlotAtIndex(s.box, s.slot).Species != 0) == occupied),
    ];

    [Theory]
    [MemberData(nameof(Saves))]
    public void Moves_ReleasesAndBoxLayout_Pass(string name)
    {
        var baseline = Load(name);
        var sav = baseline.Clone();
        var occupied = Slots(sav, occupied: true);
        var empty = Slots(sav, occupied: false);
        var none = EntityImportSettings.None;

        // Swap two boxed Pokémon.
        var (b1, s1) = occupied[0];
        var (b2, s2) = occupied[^1];
        var first = sav.GetBoxSlotAtIndex(b1, s1);
        var last = sav.GetBoxSlotAtIndex(b2, s2);
        sav.SetBoxSlotAtIndex(last, b1, s1, none);
        sav.SetBoxSlotAtIndex(first, b2, s2, none);

        // Move one into an empty slot.
        var (eb, es) = empty[0];
        sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(b1, s1), eb, es, none);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, b1, s1, none);

        // Party -> box -> back to the end of the party.
        if (sav.HasParty && sav.PartyCount >= 2)
        {
            var (pb, ps) = empty[1];
            sav.SetBoxSlotAtIndex(sav.GetPartySlotAtIndex(0), pb, ps, none);
            sav.DeletePartySlot(0);
            sav.SetPartySlotAtIndex(sav.GetBoxSlotAtIndex(pb, ps), sav.PartyCount, none);
            sav.SetBoxSlotAtIndex(sav.BlankPKM, pb, ps, none);
        }

        // Release one, switch box, rename a box.
        sav.SetBoxSlotAtIndex(sav.BlankPKM, b2, s2, none);
        sav.CurrentBox = (sav.CurrentBox + 1) % sav.BoxCount;
        if (sav is IBoxDetailName names)
            names.SetBoxName(0, "HC");

        Assert.Null(HardcoreSaveVerifier.Verify(baseline, sav, []));
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void EditedPokemon_IsRefusedAtItsSlot(string name)
    {
        var baseline = Load(name);
        var sav = baseline.Clone();
        var (box, slot) = Slots(sav, occupied: true)[0];
        var pk = sav.GetBoxSlotAtIndex(box, slot);
        pk.EXP = pk.EXP == 0 ? 1u : pk.EXP - 1;
        sav.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);

        var refusal = HardcoreSaveVerifier.Verify(baseline, sav, []);

        Assert.NotNull(refusal);
        Assert.Equal(SaveWriteRefusalKind.PokemonChanged, refusal.Kind);
        Assert.Equal((box, slot, pk.Species), (refusal.Box, refusal.Slot, refusal.Species));
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void CopiedPokemon_IsRefused(string name)
    {
        var baseline = Load(name);
        var sav = baseline.Clone();
        var (box, slot) = Slots(sav, occupied: true)[0];
        var (eb, es) = Slots(sav, occupied: false)[0];
        sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(box, slot), eb, es, EntityImportSettings.None);

        Assert.Equal(SaveWriteRefusalKind.PokemonChanged, HardcoreSaveVerifier.Verify(baseline, sav, [])?.Kind);
    }

    [Theory]
    [InlineData("gen3_emerald.sav")]
    [InlineData("gen5_white2.sav")]
    [InlineData("gen9_scarlet.main")]
    public void ChangedTrainerData_IsRefused(string name)
    {
        var baseline = Load(name);
        var sav = baseline.Clone();
        sav.Money = sav.Money == 0 ? 1u : sav.Money - 1;

        Assert.Equal(SaveWriteRefusalKind.SaveDataChanged, HardcoreSaveVerifier.Verify(baseline, sav, [])?.Kind);
    }

    [Fact]
    public void ArrivedPokemon_PassesOnceOnly()
    {
        var baseline = Load("gen5_white2.sav");
        var sav = baseline.Clone();
        var empty = Slots(sav, occupied: false);
        var arriving = sav.GetBoxSlotAtIndex(Slots(sav, occupied: true)[0].Box, Slots(sav, occupied: true)[0].Slot);
        arriving.Nickname = "ARRIVED";
        sav.SetBoxSlotAtIndex(arriving, empty[0].Box, empty[0].Slot, EntityImportSettings.None);
        var written = sav.GetBoxSlotAtIndex(empty[0].Box, empty[0].Slot);

        Assert.Equal(SaveWriteRefusalKind.PokemonChanged, HardcoreSaveVerifier.Verify(baseline, sav, [])?.Kind);
        Assert.Null(HardcoreSaveVerifier.Verify(baseline, sav, [written]));

        sav.SetBoxSlotAtIndex(written, empty[1].Box, empty[1].Slot, EntityImportSettings.None);
        Assert.Equal(SaveWriteRefusalKind.PokemonChanged, HardcoreSaveVerifier.Verify(baseline, sav, [written])?.Kind);
    }

    [Fact]
    public void Enabling_TurnsOffPokedexAndRecordUpdates_SoAPlainMoveStaysClean()
    {
        var settings = new AppSettings();
        var mode = new HardcoreMode(settings);
        var baseline = Load("gen7_ultrasun.main");
        var sav = baseline.Clone();
        var (box, slot) = Slots(sav, occupied: true)[0];
        var (eb, es) = Slots(sav, occupied: false)[0];
        try
        {
            // Core's defaults add to the trainer's records on every slot write, a plain move included.
            settings.InitializeCore();
            var withDefaults = sav.Clone();
            withDefaults.SetBoxSlotAtIndex(withDefaults.GetBoxSlotAtIndex(box, slot), eb, es);
            withDefaults.SetBoxSlotAtIndex(withDefaults.BlankPKM, box, slot);
            Assert.Equal(SaveWriteRefusalKind.SaveDataChanged, HardcoreSaveVerifier.Verify(baseline, withDefaults, [])?.Kind);

            mode.SetEnabled(true);
            sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(box, slot), eb, es);
            sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot);
            Assert.Null(HardcoreSaveVerifier.Verify(baseline, sav, []));
        }
        finally
        {
            new AppSettings().InitializeCore();
        }
    }

    [Fact]
    public void Mode_AllowsOnlyManagementActionsWhileOn()
    {
        var mode = new HardcoreMode(new AppSettings { Hardcore = { Enabled = true } });

        Assert.All(Enum.GetValues<SaveAction>(), action => Assert.Equal(
            action is SaveAction.Move or SaveAction.Release or SaveAction.Export or SaveAction.Backup,
            mode.Allows(action)));
        Assert.All(Enum.GetValues<SaveAction>(), action =>
            Assert.True(new HardcoreMode(new AppSettings()).Allows(action)));
    }

    [Fact]
    public void Mode_TurnedOnMidSession_StartsFromTheCurrentState()
    {
        var settings = new AppSettings();
        var mode = new HardcoreMode(settings);
        var sav = Load("gen5_white2.sav");
        mode.OnSaveOpened(sav);
        try
        {
            sav.Money++;
            Assert.Null(mode.Verify(sav)); // off: nothing is checked

            mode.SetEnabled(true);
            Assert.Null(mode.Verify(sav)); // the earlier edit is part of the starting point
            sav.Money++;
            Assert.Equal(SaveWriteRefusalKind.SaveDataChanged, mode.Verify(sav)?.Kind);
        }
        finally
        {
            new AppSettings().InitializeCore();
        }
    }

    [Fact]
    public void Mode_WithoutABaseline_FailsClosed()
    {
        var mode = new HardcoreMode(new AppSettings { Hardcore = { Enabled = true } });
        var sav = Load("gen5_white2.sav");

        Assert.Equal(SaveWriteRefusalKind.CannotVerify, mode.Verify(sav)?.Kind);
    }

    [Fact]
    public async Task SaveFileService_RefusesAnEditWithoutBackingUpOrWriting_AndRebaselinesAfterAMove()
    {
        var path = Path.Combine(_tempDir, "game.sav");
        File.Copy(FixturePath("gen5_white2.sav"), path);
        var original = await File.ReadAllBytesAsync(path);
        var settings = new AppSettings { Hardcore = { Enabled = true } };
        var backups = new SaveBackupService(new FakeAppPaths());
        var mode = new HardcoreMode(settings);
        var service = new SaveFileService(backups, settings, mode);
        Assert.True(await service.LoadSaveFileAsync(path));
        var sav = service.CurrentSave!;

        sav.Money++;
        Assert.False(await service.SaveFileAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(backups.ListBackups(SaveIdentity.Compute(path)).Entries);

        sav.Money--;
        var (box, slot) = Slots(sav, occupied: true)[0];
        var (eb, es) = Slots(sav, occupied: false)[0];
        sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(box, slot), eb, es, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot, EntityImportSettings.None);
        Assert.True(await service.SaveFileAsync());

        // The written state is the new baseline: moving back is still a move.
        sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(eb, es), box, slot, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, eb, es, EntityImportSettings.None);
        Assert.Null(mode.Verify(sav));
        Assert.True(await service.SaveFileAsync());
    }
}
