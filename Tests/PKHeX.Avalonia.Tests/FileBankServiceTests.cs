using System;
using System.IO;
using System.Linq;
using System.Text;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Infrastructure;
using PKHeX.Infrastructure.Bank;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Covers the Bank's flat-file storage engine (Phase 0/1 of the Bank system): deposit/withdraw-shaped
/// operations, box auto-growth, persistence across process restarts (a new <see cref="FileBankService"/>
/// instance pointed at the same root), atomic-write crash-safety (a stray <c>.tmp</c> left behind must
/// never corrupt a later load), and the "fail safe, never silently reset to empty" read-only recovery
/// path. Uses a throwaway temp directory (<see cref="FakeAppPaths"/>) so nothing touches real user data.
/// </summary>
public sealed class FileBankServiceTests : IDisposable
{
    private readonly FakeAppPaths _paths = new();
    private readonly string _root;

    public FileBankServiceTests()
    {
        _root = Path.GetDirectoryName(_paths.DataDirectory)!;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    private string BankRoot => Path.Combine(_paths.DataDirectory, "bank");
    private string IndexPath => Path.Combine(BankRoot, "index.json");

    private static PKM MakePk9(ushort species, string nickname, bool shiny = false)
    {
        var sav = new SAV9SV();
        var pk = (PK9)sav.BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        pk.CurrentLevel = 50;
        if (shiny)
            pk.PID = (uint)((pk.TID16 ^ pk.SID16) << 16); // cheap deterministic shiny PID for this save's IDs
        return pk;
    }

    private static (byte[] raw, BankEntryInfo info) Deposit(PKM pk, string source = "test") =>
        (BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, source));

    [Fact]
    public void Add_PlacesInFirstEmptySlot_AndPersistsRawBytes()
    {
        var bank = new FileBankService(_paths, slotsPerBox: 30);
        var (raw, info) = Deposit(MakePk9(6, "Charizard"));

        var result = bank.Add(raw, info);

        Assert.True(result.Success);
        Assert.NotNull(result.Entry);
        Assert.Equal(0, result.Entry!.Box);
        Assert.Equal(0, result.Entry!.Slot);
        Assert.Equal(1, bank.BoxCount);

        var stored = bank.GetRawData(result.Entry!.Id);
        Assert.Equal(raw, stored);
    }

    [Fact]
    public void Add_FillsSecondSlotBeforeGrowing()
    {
        var bank = new FileBankService(_paths, slotsPerBox: 2);
        var r1 = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info);
        var r2 = bank.Add(Deposit(MakePk9(2, "B")).raw, Deposit(MakePk9(2, "B")).info);

        Assert.Equal(0, r1.Entry!.Box);
        Assert.Equal(0, r1.Entry!.Slot);
        Assert.Equal(0, r2.Entry!.Box);
        Assert.Equal(1, r2.Entry!.Slot);
        Assert.Equal(1, bank.BoxCount);
    }

    [Fact]
    public void Add_GrowsNewBox_WhenExistingBoxesAreFull()
    {
        var bank = new FileBankService(_paths, slotsPerBox: 2);
        bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info);
        bank.Add(Deposit(MakePk9(2, "B")).raw, Deposit(MakePk9(2, "B")).info);

        var third = bank.Add(Deposit(MakePk9(3, "C")).raw, Deposit(MakePk9(3, "C")).info);

        Assert.True(third.Success);
        Assert.Equal(1, third.Entry!.Box);
        Assert.Equal(0, third.Entry!.Slot);
        Assert.Equal(2, bank.BoxCount);
    }

    [Fact]
    public void AddBox_IncreasesBoxCount_EvenWhenEmpty()
    {
        var bank = new FileBankService(_paths);
        Assert.Equal(1, bank.BoxCount);

        var newIndex = bank.AddBox();

        Assert.Equal(1, newIndex);
        Assert.Equal(2, bank.BoxCount);
    }

    [Fact]
    public void Persistence_AcrossNewInstance_SeesSameEntries()
    {
        var bank1 = new FileBankService(_paths);
        var (raw, info) = Deposit(MakePk9(25, "Pikachu"));
        var added = bank1.Add(raw, info);
        Assert.True(added.Success);

        // Simulate an app restart: a brand-new service instance pointed at the same root.
        var bank2 = new FileBankService(_paths);

        var all = bank2.GetAll();
        Assert.Single(all);
        Assert.Equal(added.Entry!.Id, all[0].Id);
        Assert.Equal("Pikachu", all[0].Info.Nickname);
        Assert.Equal(raw, bank2.GetRawData(added.Entry!.Id));
    }

    [Fact]
    public void Move_ToEmptySlot_Succeeds()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;

        var result = bank.Move(entry.Id, 0, 5);

        Assert.True(result.Success);
        Assert.Equal(5, bank.Find(entry.Id)!.Slot);
    }

    [Fact]
    public void Move_ToOccupiedSlot_FailsWithoutSwap()
    {
        var bank = new FileBankService(_paths);
        var e1 = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;
        var e2 = bank.Add(Deposit(MakePk9(2, "B")).raw, Deposit(MakePk9(2, "B")).info).Entry!;

        var result = bank.Move(e2.Id, e1.Box, e1.Slot);

        Assert.False(result.Success);
        Assert.Equal(e2.Slot, bank.Find(e2.Id)!.Slot); // unchanged
    }

    [Fact]
    public void Move_ToOccupiedSlot_SwapsWhenRequested()
    {
        var bank = new FileBankService(_paths);
        var e1 = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!; // slot 0
        var e2 = bank.Add(Deposit(MakePk9(2, "B")).raw, Deposit(MakePk9(2, "B")).info).Entry!; // slot 1

        var result = bank.Move(e2.Id, e1.Box, e1.Slot, swap: true);

        Assert.True(result.Success);
        Assert.Equal(0, bank.Find(e2.Id)!.Slot);
        Assert.Equal(1, bank.Find(e1.Id)!.Slot);
    }

    [Fact]
    public void Replace_UpdatesBytesAndInfo_KeepsBoxAndSlot()
    {
        var bank = new FileBankService(_paths);
        var original = Deposit(MakePk9(1, "Before"));
        var entry = bank.Add(original.raw, original.info).Entry!;

        var updated = Deposit(MakePk9(1, "After"));
        var result = bank.Replace(entry.Id, updated.raw, updated.info);

        Assert.True(result.Success);
        var reloaded = bank.Find(entry.Id)!;
        Assert.Equal(entry.Box, reloaded.Box);
        Assert.Equal(entry.Slot, reloaded.Slot);
        Assert.Equal("After", reloaded.Info.Nickname);
        Assert.Equal(updated.raw, bank.GetRawData(entry.Id));
    }

    [Fact]
    public void Remove_DeletesEntryAndRawFile()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;
        var binPath = Path.Combine(BankRoot, $"{entry.Id:N}.bin");
        Assert.True(File.Exists(binPath));

        var result = bank.Remove(entry.Id);

        Assert.True(result.Success);
        Assert.Null(bank.Find(entry.Id));
        Assert.False(File.Exists(binPath));
    }

    [Fact]
    public void GetRawData_Reconstructs_ExactOriginalType_ViaFormat()
    {
        var bank = new FileBankService(_paths);
        var pk = MakePk9(150, "Mewtwo");
        var (raw, info) = Deposit(pk);
        var entry = bank.Add(raw, info).Entry!;

        var storedRaw = bank.GetRawData(entry.Id)!;
        var reconstructed = BankEntryInfoFactory.TryReconstruct(storedRaw, entry.Info);

        Assert.NotNull(reconstructed);
        Assert.IsType<PK9>(reconstructed);
        Assert.Equal(150, reconstructed!.Species);
        Assert.Equal("Mewtwo", reconstructed.Nickname);
    }

    [Fact]
    public void TryReconstruct_KeepsLuminescentPokemonBeyondTheSinnohDex()
    {
        // Luminescent Platinum's PB8LUMI shares PB8's layout; byte detection caps species at the Sinnoh
        // dex, so only the recorded format brings back e.g. Greninja (658) as a PB8LUMI.
        var pk = new PB8LUMI { Species = 658, Nickname = "Frogger", IsNicknamed = true };
        pk.RefreshChecksum();
        var (raw, info) = Deposit(pk);
        Assert.Equal("pb8lumi", info.Format);

        var reconstructed = BankEntryInfoFactory.TryReconstruct(raw, info);

        Assert.IsType<PB8LUMI>(reconstructed);
        Assert.Equal(658, reconstructed!.Species);
        Assert.Equal("Frogger", reconstructed.Nickname);
    }

    [Fact]
    public void TryReconstruct_HonorsRecordedFormat_ForPk5ThatLooksLikePk4()
    {
        // A PK5 with a Gen 4-range version byte and a low met location trips Core's PK4/PK5 byte
        // heuristic; reconstruction must trust the recorded "pk5" instead of re-guessing.
        var pk = new PK5 { Species = 25, Nickname = "Sparky", IsNicknamed = true, Version = GameVersion.HG };
        pk.RefreshChecksum();
        var (raw, info) = Deposit(pk);
        Assert.IsType<PK4>(FileUtil.GetSupportedFile(raw, info.Format)); // the heuristic misreads it

        var reconstructed = BankEntryInfoFactory.TryReconstruct(raw, info);

        Assert.IsType<PK5>(reconstructed);
        Assert.Equal("Sparky", reconstructed!.Nickname);
        Assert.Equal(raw, BankEntryInfoFactory.GetRawData(reconstructed));
    }

    [Fact]
    public void BoxName_And_Wallpaper_RoundTrip_AndFallBackToDefaults()
    {
        var bank = new FileBankService(_paths);

        Assert.Equal(BoxDetailNameExtensions.GetDefaultBoxName(0), bank.GetBoxName(0));
        Assert.Equal(BankWallpaper.Default, bank.GetBoxWallpaper(0));

        bank.SetBoxName(0, "Legendaries");
        bank.SetBoxWallpaper(0, BankWallpaper.Art("sv:box_wp01sv"));

        Assert.Equal("Legendaries", bank.GetBoxName(0));
        Assert.Equal(BankWallpaperKind.Art, bank.GetBoxWallpaper(0).Kind);
        Assert.Equal("sv:box_wp01sv", bank.GetBoxWallpaper(0).Value);

        // Persists across a new instance too (side files use the same root).
        var bank2 = new FileBankService(_paths);
        Assert.Equal("Legendaries", bank2.GetBoxName(0));
    }

    [Fact]
    public void CorruptBoxNamesSideFile_FallsBackToDefaults_NeverThrows_AndEntriesAreUnaffected()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;

        File.WriteAllText(Path.Combine(BankRoot, "box-names.json"), "{ not valid json ]");

        var bank2 = new FileBankService(_paths);

        Assert.Equal(BoxDetailNameExtensions.GetDefaultBoxName(0), bank2.GetBoxName(0));
        Assert.Single(bank2.GetAll()); // decoration corruption never touches entries
        Assert.Equal(entry.Id, bank2.GetAll()[0].Id);
    }

    [Fact]
    public void CorruptPrimaryIndex_WithValidBackup_RecoversFromBackup_AndWarns()
    {
        var bank = new FileBankService(_paths);
        var first = bank.Add(Deposit(MakePk9(1, "First")).raw, Deposit(MakePk9(1, "First")).info).Entry!;
        // Second write causes the first write's index.json to be copied to index.json.bak before being replaced.
        bank.Add(Deposit(MakePk9(2, "Second")).raw, Deposit(MakePk9(2, "Second")).info);

        // Corrupt the primary only; .bak (holding just "First") is untouched.
        File.WriteAllText(IndexPath, "{ this is not valid json");

        var recovered = new FileBankService(_paths);

        Assert.False(recovered.IsReadOnly);
        Assert.NotEmpty(recovered.Warnings);
        Assert.Contains(recovered.Warnings, w => w.Contains("backup", StringComparison.OrdinalIgnoreCase));
        var all = recovered.GetAll();
        Assert.Single(all);
        Assert.Equal(first.Id, all[0].Id);
    }

    [Fact]
    public void CorruptPrimaryAndBackup_OpensReadOnly_NeverResetsToEmptySilently_AndLeavesBinFilesUntouched()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;
        var binPath = Path.Combine(BankRoot, $"{entry.Id:N}.bin");

        File.WriteAllText(IndexPath, "{ corrupt");
        File.WriteAllText(IndexPath + ".bak", "{ also corrupt");

        var readOnlyBank = new FileBankService(_paths);

        Assert.True(readOnlyBank.IsReadOnly);
        Assert.NotEmpty(readOnlyBank.Warnings);
        // The entity blob is never touched just because the index couldn't be read.
        Assert.True(File.Exists(binPath));

        // Fail-safe: every mutating call refuses instead of risking data loss.
        var addResult = readOnlyBank.Add(Deposit(MakePk9(2, "B")).raw, Deposit(MakePk9(2, "B")).info);
        Assert.False(addResult.Success);
        var removeResult = readOnlyBank.Remove(entry.Id);
        Assert.False(removeResult.Success);
    }

    [Fact]
    public void StrayTmpFileFromAnInterruptedWrite_IsIgnoredOnLoad_CrashSafetySimulation()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;

        // Simulate a crash between "tmp written" and "rename over index.json": a stray, even
        // garbage, .tmp file must never be mistaken for the real index.
        File.WriteAllText(IndexPath + ".tmp", "{ garbage left over from an interrupted write");

        var reloaded = new FileBankService(_paths);

        Assert.False(reloaded.IsReadOnly);
        var all = reloaded.GetAll();
        Assert.Single(all);
        Assert.Equal(entry.Id, all[0].Id);
    }

    [Fact]
    public void Reload_RecoversFromReadOnly_AfterTheFileIsFixedExternally()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info).Entry!;

        // Force a read-only state by corrupting both copies in-process.
        File.WriteAllText(IndexPath, "{ corrupt");
        File.WriteAllText(IndexPath + ".bak", "{ also corrupt");
        bank.Reload();
        Assert.True(bank.IsReadOnly);

        // Fix it externally (e.g. the user restored a good backup via the Backup Manager UI).
        var goodJson = "{\"SchemaVersion\":1,\"BoxCount\":1,\"Entries\":[]}";
        File.WriteAllText(IndexPath, goodJson);
        bank.Reload();

        Assert.False(bank.IsReadOnly);
        Assert.Empty(bank.GetAll());
    }

    [Fact]
    public void IndexWrites_AreHookedIntoTheBackupManagerMechanism()
    {
        var backupService = new SaveBackupService(_paths);
        var bank = new FileBankService(_paths, backupService);

        bank.Add(Deposit(MakePk9(1, "A")).raw, Deposit(MakePk9(1, "A")).info);

        var backups = backupService.ListBackups(FileBankService.BackupIdentity);
        Assert.NotEmpty(backups.Entries);

        var content = backupService.ReadBackup(backups.Entries[0]);
        Assert.Contains("SchemaVersion", Encoding.UTF8.GetString(content));
    }
}
