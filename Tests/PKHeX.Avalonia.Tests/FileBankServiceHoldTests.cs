using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Infrastructure.Bank;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Storage-level coverage for the Bank's hold-until-saved markers, the hardened index restore, and
/// several app instances sharing one Bank directory.
/// </summary>
public sealed class FileBankServiceHoldTests : IDisposable
{
    private readonly FakeAppPaths _paths = new();

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_paths.DataDirectory)!, recursive: true); }
        catch { /* best effort */ }
    }

    private string BankRoot => Path.Combine(_paths.DataDirectory, "bank");
    private string IndexPath => Path.Combine(BankRoot, "index.json");
    private string BlobPath(Guid id) => Path.Combine(BankRoot, $"{id:N}.bin");

    private static (byte[] Raw, BankEntryInfo Info) Make(ushort species, string nickname = "")
    {
        var sav = new SAV9SV();
        var pk = (PK9)sav.BlankPKM;
        pk.Species = species;
        pk.CurrentLevel = 20;
        if (nickname.Length > 0)
            pk.Nickname = nickname;
        return (BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "test"));
    }

    private static BankPendingWithdrawal Hold(Guid token) =>
        new(token, "Scarlet (Test)", "/tmp/test.sav", 0, 3, DateTimeOffset.UtcNow);

    [Fact]
    public void Construction_HasNoDiskSideEffects_UntilFirstUse()
    {
        var bank = new FileBankService(_paths);
        Assert.False(Directory.Exists(BankRoot));

        _ = bank.GetAll();
        Assert.True(Directory.Exists(BankRoot));
        Assert.False(File.Exists(IndexPath)); // reading an empty Bank writes nothing
    }

    [Fact]
    public void Hold_IsPersisted_BlocksSecondHoldAndRemove_AndKeepsTheBlob()
    {
        var bank = new FileBankService(_paths);
        var (raw, info) = Make(25);
        var entry = bank.Add(raw, info).Entry!;
        var token = Guid.NewGuid();

        Assert.True(bank.MarkPendingWithdrawal(entry.Id, Hold(token)).Success);

        var held = bank.Find(entry.Id)!;
        Assert.NotNull(held.Pending);
        Assert.Equal(token, held.Pending!.Token);
        Assert.Equal(Environment.ProcessId, held.Pending.OwnerProcessId); // stamped by the service
        Assert.Contains(token.ToString(), File.ReadAllText(IndexPath)); // persisted in the index

        var second = bank.MarkPendingWithdrawal(entry.Id, Hold(Guid.NewGuid()));
        Assert.False(second.Success);
        Assert.Equal(BankErrorKind.PendingWithdrawal, second.Kind);

        var remove = bank.Remove(entry.Id);
        Assert.False(remove.Success);
        Assert.Equal(BankErrorKind.PendingWithdrawal, remove.Kind);
        Assert.True(File.Exists(BlobPath(entry.Id)));
    }

    [Fact]
    public void Complete_RequiresTheMatchingToken_ThenRemovesEntryAndBlob()
    {
        var bank = new FileBankService(_paths);
        var (raw, info) = Make(1);
        var entry = bank.Add(raw, info).Entry!;
        var token = Guid.NewGuid();
        bank.MarkPendingWithdrawal(entry.Id, Hold(token));

        var wrong = bank.CompletePendingWithdrawal(entry.Id, Guid.NewGuid());
        Assert.False(wrong.Success);
        Assert.NotNull(bank.Find(entry.Id));
        Assert.True(File.Exists(BlobPath(entry.Id)));

        Assert.True(bank.CompletePendingWithdrawal(entry.Id, token).Success);
        Assert.Null(bank.Find(entry.Id));
        Assert.False(File.Exists(BlobPath(entry.Id)));
    }

    [Fact]
    public void Release_ClearsTheHold_KeepsTheEntry_AndIsIdempotent()
    {
        var bank = new FileBankService(_paths);
        var (raw, info) = Make(4);
        var entry = bank.Add(raw, info).Entry!;
        var token = Guid.NewGuid();
        bank.MarkPendingWithdrawal(entry.Id, Hold(token));

        Assert.True(bank.ReleasePendingWithdrawal(entry.Id, token).Success);
        Assert.Null(bank.Find(entry.Id)!.Pending);
        Assert.True(bank.ReleasePendingWithdrawal(entry.Id, token).Success);
        Assert.Equal(raw, bank.GetRawData(entry.Id));

        // Completion after release is refused, so a stale tracker can never delete a released entry.
        Assert.False(bank.CompletePendingWithdrawal(entry.Id, token).Success);
        Assert.NotNull(bank.Find(entry.Id));
    }

    [Fact]
    public void Restart_ReleasesHoldsOfTheEndedSession_AndKeepsThePokemon()
    {
        var first = new FileBankService(_paths);
        var (raw, info) = Make(7);
        var entry = first.Add(raw, info).Entry!;
        first.MarkPendingWithdrawal(entry.Id, Hold(Guid.NewGuid()));

        // A new service instance = the next app run (the previous run's in-memory tracker is gone).
        var restarted = new FileBankService(_paths);
        var reloaded = restarted.Find(entry.Id);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Pending);
        Assert.Equal(raw, restarted.GetRawData(entry.Id));
        Assert.Contains(restarted.Warnings, w => w.Contains("previous session", StringComparison.Ordinal));
        Assert.DoesNotContain("\"Token\"", File.ReadAllText(IndexPath)); // the release was persisted
    }

    [Fact]
    public void TwoInstances_SharingOneBank_NeverOverwriteEachOthersDeposits()
    {
        var windowA = new FileBankService(_paths);
        var windowB = new FileBankService(_paths);
        _ = windowA.GetAll();
        _ = windowB.GetAll(); // both loaded the same (empty) state

        var a = windowA.Add(Make(10).Raw, Make(10).Info);
        var b = windowB.Add(Make(11).Raw, Make(11).Info);

        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.NotEqual((a.Entry!.Box, a.Entry.Slot), (b.Entry!.Box, b.Entry.Slot));

        var fresh = new FileBankService(_paths);
        var ids = fresh.GetAll().Select(e => e.Id).ToHashSet();
        Assert.Contains(a.Entry.Id, ids);
        Assert.Contains(b.Entry.Id, ids);

        // A read in the other instance picks up the external change too.
        Assert.Equal(2, windowA.GetAll().Count);
    }

    [Fact]
    public void Restore_BacksUpCurrentIndexFirst_WritesAtomically_AndDropsHolds()
    {
        var backups = new RecordingBackupService();
        var bank = new FileBankService(_paths, backups);
        var old = bank.Add(Make(25, "Old").Raw, Make(25, "Old").Info).Entry!;

        var token = Guid.NewGuid();
        bank.MarkPendingWithdrawal(old.Id, Hold(token));
        var heldIndex = File.ReadAllBytes(IndexPath); // includes the hold
        var added = bank.Add(Make(26, "New").Raw, Make(26, "New").Info).Entry!;
        var currentIndex = File.ReadAllBytes(IndexPath);
        backups.Created.Clear();

        var result = bank.RestoreIndexFromBackup(heldIndex);

        Assert.True(result.Success, result.Error);
        Assert.Single(backups.Created);
        Assert.Equal(currentIndex, backups.Created[0]); // the index being replaced was preserved first
        Assert.False(File.Exists(IndexPath + ".tmp"));

        Assert.NotNull(bank.Find(old.Id));
        Assert.Null(bank.Find(old.Id)!.Pending); // holds never survive a restore
        Assert.Null(bank.Find(added.Id));
        Assert.True(File.Exists(BlobPath(added.Id))); // un-listed, but its data is not destroyed

        var reread = new FileBankService(_paths);
        Assert.Equal(new[] { old.Id }, reread.GetAll().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Restore_WithoutBackupService_KeepsALocalPreRestoreCopy()
    {
        var bank = new FileBankService(_paths);
        bank.Add(Make(1).Raw, Make(1).Info);
        var first = File.ReadAllBytes(IndexPath);
        bank.Add(Make(2).Raw, Make(2).Info);
        var current = File.ReadAllBytes(IndexPath);

        Assert.True(bank.RestoreIndexFromBackup(first).Success);
        Assert.Equal(current, File.ReadAllBytes(IndexPath + ".pre-restore"));
        Assert.Single(bank.GetAll());
    }

    [Fact]
    public void Restore_OfAnIndexReferencingMissingBlobs_WarnsInsteadOfFailing()
    {
        var bank = new FileBankService(_paths);
        var kept = bank.Add(Make(1).Raw, Make(1).Info).Entry!;
        var withdrawn = bank.Add(Make(2).Raw, Make(2).Info).Entry!;
        var snapshot = File.ReadAllBytes(IndexPath);
        Assert.True(bank.Remove(withdrawn.Id).Success); // its blob is deleted

        var result = bank.RestoreIndexFromBackup(snapshot);

        Assert.True(result.Success);
        Assert.Equal(2, bank.GetAll().Count);
        Assert.Equal(new[] { withdrawn.Id }, bank.GetEntriesWithMissingData().ToArray());
        Assert.Contains(bank.Warnings, w => w.Contains("missing on disk", StringComparison.Ordinal));
        Assert.Null(bank.GetRawData(withdrawn.Id));
        Assert.NotNull(bank.GetRawData(kept.Id));
    }

    [Fact]
    public void Restore_RefusesGarbage_AndChangesNothing()
    {
        var bank = new FileBankService(_paths);
        var entry = bank.Add(Make(1).Raw, Make(1).Info).Entry!;
        var before = File.ReadAllBytes(IndexPath);

        var result = bank.RestoreIndexFromBackup(Encoding.UTF8.GetBytes("{ definitely not an index"));

        Assert.False(result.Success);
        Assert.Equal(BankErrorKind.InvalidData, result.Kind);
        Assert.Equal(before, File.ReadAllBytes(IndexPath));
        Assert.NotNull(bank.Find(entry.Id));
    }

    [Fact]
    public void ReadOnlyBank_RefusesHolds_WithAReadOnlyKind()
    {
        Directory.CreateDirectory(BankRoot);
        File.WriteAllText(IndexPath, "{corrupt");
        File.WriteAllText(IndexPath + ".bak", "{also corrupt");

        var bank = new FileBankService(_paths);

        Assert.True(bank.IsReadOnly);
        var add = bank.Add(Make(1).Raw, Make(1).Info);
        Assert.False(add.Success);
        Assert.Equal(BankErrorKind.ReadOnly, add.Kind);
        Assert.Equal(BankErrorKind.ReadOnly, bank.MarkPendingWithdrawal(Guid.NewGuid(), Hold(Guid.NewGuid())).Kind);
    }

    [Fact]
    public void ClearingABoxName_FallsBackToTheDefault()
    {
        var bank = new FileBankService(_paths);
        bank.SetBoxName(0, "  Favourites ");
        Assert.Equal("Favourites", bank.GetBoxName(0));

        bank.SetBoxName(0, "   ");
        Assert.Equal(BoxDetailNameExtensions.GetDefaultBoxName(0), bank.GetBoxName(0));
    }

    private sealed class RecordingBackupService : ISaveBackupService
    {
        public List<byte[]> Created { get; } = [];

        public SaveBackupEntry? CreateBackup(string saveIdentity, byte[] data, int maxBackups)
        {
            Created.Add(data.ToArray());
            return new SaveBackupEntry($"/tmp/{Created.Count}.bak", $"{Created.Count}.bak", DateTime.UtcNow, data.Length);
        }

        public BackupListResult ListBackups(string saveIdentity) => new([], []);
        public byte[] ReadBackup(SaveBackupEntry entry) => [];
        public void DeleteBackup(SaveBackupEntry entry) { }
        public string GetBackupFolder(string saveIdentity) => Path.GetTempPath();
    }
}
