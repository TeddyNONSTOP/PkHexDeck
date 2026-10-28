using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Infrastructure.Bank;

/// <summary>On-disk envelope for the Bank's <c>index.json</c>.</summary>
internal sealed record BankIndexFile(int SchemaVersion, int BoxCount, List<BankEntry> Entries);

/// <summary>
/// Flat-file <see cref="IBankService"/>: one raw-bytes <c>.bin</c> file per Pokémon (exact decrypted
/// native-format bytes, untouched — never normalized to a universal representation) plus one
/// <c>index.json</c> for the whole vault. The index is written atomically (tmp file, then a backup
/// copy of the previous good version, then an atomic rename over the real file) and, on load, a
/// primary file that fails to parse falls back to that backup copy; if both are unreadable the Bank
/// opens read-only rather than silently resetting to empty — existing <c>.bin</c> files are never
/// touched just because the index couldn't be read. Box names/wallpapers are decoration, stored in
/// separate small side files (<see cref="BoxKeyedFile{T}"/>) that can never block an entry mutation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Several app instances may share one Bank</b> (nothing prevents opening two app windows/processes).
/// Every mutation therefore takes an exclusive <c>index.lock</c> file lock, re-reads the on-disk index
/// if another instance changed it since this instance last read/wrote it, and only then applies its
/// change — so one instance can never overwrite another's deposit with a stale in-memory copy. Reads
/// cheaply re-check the index file's timestamp/length and pick up external changes too.
/// </para>
/// <para>
/// Nothing touches the disk until the Bank is first used (construction is free of side effects), so
/// merely composing the app — including in tests — never creates a Bank directory.
/// </para>
/// </remarks>
public sealed class FileBankService : IBankService
{
    /// <summary>Identity this service's index backups are grouped under via <see cref="ISaveBackupService"/> — the Bank is not a save file, so it gets its own fixed identity rather than one derived from a path.</summary>
    public const string BackupIdentity = BankConstants.BackupIdentity;

    private const int SchemaVersion = 1;
    private const string IndexFileName = "index.json";
    private const string LockFileName = "index.lock";
    private const string BackupSuffix = ".bak";
    private const string PreRestoreSuffix = ".pre-restore";
    private const string BoxNamesFileName = "box-names.json";
    private const string BoxWallpapersFileName = "box-wallpapers.json";

    private static readonly JsonSerializerOptions IndexOptions = new() { WriteIndented = true };
    private static readonly (int Id, long StartTicks) CurrentProcess = GetCurrentProcessIdentity();

    private readonly string _root;
    private readonly string _indexPath;
    private readonly string _lockPath;
    private readonly ISaveBackupService? _backupService;
    private readonly int _backupRetention;
    private readonly object _gate = new();
    private readonly Guid _instanceId = Guid.NewGuid();

    private readonly Dictionary<Guid, BankEntry> _entries = new();
    private readonly List<string> _warnings = new();
    private int _boxCount = 1;
    private bool _indexUnreadable;
    private bool _loaded;

    // What this instance last saw on disk: a cheap stamp for reads, a content hash for mutations.
    private (DateTime WriteTimeUtc, long Length)? _lastStamp;
    private string? _lastIndexHash;

    private readonly BoxKeyedFile<string> _boxNames;
    private readonly BoxKeyedFile<BankWallpaper> _boxWallpapers;

    public FileBankService(IAppPaths paths, ISaveBackupService? backupService = null, int slotsPerBox = 30, int backupRetention = 20)
    {
        _root = Path.Combine(paths.DataDirectory, "bank");
        _indexPath = Path.Combine(_root, IndexFileName);
        _lockPath = Path.Combine(_root, LockFileName);
        _backupService = backupService;
        SlotsPerBox = slotsPerBox;
        _backupRetention = backupRetention;
        _boxNames = new BoxKeyedFile<string>(Path.Combine(_root, BoxNamesFileName));
        _boxWallpapers = new BoxKeyedFile<BankWallpaper>(Path.Combine(_root, BoxWallpapersFileName));
    }

    public int SlotsPerBox { get; }

    public int BoxCount
    {
        get
        {
            lock (_gate)
            {
                EnsureCurrent();
                return _boxCount;
            }
        }
    }

    public bool IsReadOnly
    {
        get
        {
            lock (_gate)
            {
                EnsureCurrent();
                return _indexUnreadable;
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate)
            {
                EnsureCurrent();
                return _warnings.ToList();
            }
        }
    }

    public IReadOnlyList<BankEntry> GetAll()
    {
        lock (_gate)
        {
            EnsureCurrent();
            return _entries.Values.OrderBy(e => e.Box).ThenBy(e => e.Slot).ToList();
        }
    }

    public IReadOnlyList<BankEntry> GetBox(int box)
    {
        lock (_gate)
        {
            EnsureCurrent();
            return _entries.Values.Where(e => e.Box == box).OrderBy(e => e.Slot).ToList();
        }
    }

    public BankEntry? Find(Guid id)
    {
        lock (_gate)
        {
            EnsureCurrent();
            return _entries.TryGetValue(id, out var entry) ? entry : null;
        }
    }

    public byte[]? GetRawData(Guid id)
    {
        try
        {
            return File.ReadAllBytes(GetEntityPath(id));
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyCollection<Guid> GetEntriesWithMissingData()
    {
        lock (_gate)
        {
            EnsureCurrent();
            return _entries.Keys.Where(id => !File.Exists(GetEntityPath(id))).ToList();
        }
    }

    public BankAddResult Add(byte[] rawData, BankEntryInfo info, int? box = null, int? slot = null) =>
        AddCore(rawData, info, box, slot, pending: null);

    public BankAddResult AddPendingDeposit(byte[] rawData, BankEntryInfo info, BankPendingWithdrawal hold)
    {
        ArgumentNullException.ThrowIfNull(hold);
        return AddCore(rawData, info, null, null, Stamp(hold, BankHoldKind.Deposit));
    }

    private BankAddResult AddCore(byte[] rawData, BankEntryInfo info, int? box, int? slot, BankPendingWithdrawal? pending)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return BankAddResult.Fail(failure.Error ?? string.Empty, failure.Kind);

            int targetBox, targetSlot;
            if (box is { } b && slot is { } s)
            {
                if (b < 0 || s < 0 || s >= SlotsPerBox)
                    return BankAddResult.Fail("Invalid box/slot.", BankErrorKind.InvalidSlot);
                if (IsOccupied(b, s))
                    return BankAddResult.Fail("That Bank slot is already occupied.", BankErrorKind.Occupied);
                targetBox = b;
                targetSlot = s;
            }
            else if (!TryFindFirstEmpty(out targetBox, out targetSlot))
            {
                targetBox = _boxCount; // will exist once the mutation below grows BoxCount
                targetSlot = 0;
            }

            var id = Guid.NewGuid();
            var path = GetEntityPath(id);
            try
            {
                AtomicFileWriter.WriteAllBytes(path, rawData);
            }
            catch (Exception ex)
            {
                return BankAddResult.Fail($"Could not write the entity file: {ex.Message}");
            }

            var entry = new BankEntry(id, targetBox, targetSlot, info, DateTimeOffset.UtcNow, pending);
            var result = Commit(() =>
            {
                if (targetBox >= _boxCount)
                    _boxCount = targetBox + 1;
                _entries[id] = entry;
            });

            if (!result.Success)
            {
                TryDeleteFile(path);
                return BankAddResult.Fail(result.Error ?? "Unknown error.", result.Kind);
            }

            return BankAddResult.Ok(entry);
        }
    }

    public BankOperationResult Move(Guid id, int box, int slot, bool swap = false)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (box < 0 || slot < 0 || slot >= SlotsPerBox)
                return BankOperationResult.Fail("Invalid destination box/slot.", BankErrorKind.InvalidSlot);

            var occupant = _entries.Values.FirstOrDefault(e => e.Box == box && e.Slot == slot && e.Id != id);
            if (occupant is not null && !swap)
                return BankOperationResult.Fail("The destination slot is already occupied.", BankErrorKind.Occupied);

            return Commit(() =>
            {
                if (box >= _boxCount)
                    _boxCount = box + 1;

                if (occupant is not null)
                    _entries[occupant.Id] = occupant with { Box = entry.Box, Slot = entry.Slot };

                _entries[id] = entry with { Box = box, Slot = slot };
            });
        }
    }

    public BankOperationResult Replace(Guid id, byte[] rawData, BankEntryInfo info)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (entry.Pending is not null)
                return PendingRefusal(entry);

            var path = GetEntityPath(id);
            byte[]? previousData = null;
            try
            {
                if (File.Exists(path))
                    previousData = File.ReadAllBytes(path);
                AtomicFileWriter.WriteAllBytes(path, rawData);
            }
            catch (Exception ex)
            {
                return BankOperationResult.Fail($"Could not write the entity file: {ex.Message}");
            }

            var result = Commit(() => _entries[id] = entry with { Info = info });
            if (!result.Success && previousData is not null)
                TryWriteFile(path, previousData);

            return result;
        }
    }

    public BankOperationResult Remove(Guid id)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (entry.Pending is not null)
                return PendingRefusal(entry);

            return RemoveCore(id);
        }
    }

    public BankOperationResult MarkPendingWithdrawal(Guid id, BankPendingWithdrawal pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (entry.Pending is not null)
                return PendingRefusal(entry);

            var stamped = Stamp(pending, BankHoldKind.Withdrawal);

            // Holds churn on every withdrawal and save; they are not worth a Backup Manager snapshot
            // each (that would push real content changes out of the retention window).
            return Commit(() => _entries[id] = entry with { Pending = stamped }, createBackup: false);
        }
    }

    public BankOperationResult ReleasePendingWithdrawal(Guid id, Guid token)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry) || entry.Pending is not { Kind: BankHoldKind.Withdrawal } held || held.Token != token)
                return BankOperationResult.Succeeded; // already released/completed elsewhere: nothing to do

            return Commit(() => _entries[id] = entry with { Pending = null }, createBackup: false);
        }
    }

    public BankOperationResult CompletePendingWithdrawal(Guid id, Guid token)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (entry.Pending is not { Kind: BankHoldKind.Withdrawal } held || held.Token != token)
                return BankOperationResult.Fail("That entry is not held by this withdrawal, so it was kept in the Bank.", BankErrorKind.PendingWithdrawal);

            return RemoveCore(id);
        }
    }

    public BankOperationResult ConfirmPendingDeposit(Guid id, Guid token)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry) || entry.Pending is not { Kind: BankHoldKind.Deposit } held || held.Token != token)
                return BankOperationResult.Succeeded; // already confirmed (or released on restart): the entry is kept either way

            return Commit(() => _entries[id] = entry with { Pending = null }, createBackup: false);
        }
    }

    public BankOperationResult CancelPendingDeposit(Guid id, Guid token)
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is { } failure)
                return failure;
            if (!_entries.TryGetValue(id, out var entry))
                return NotFound();
            if (entry.Pending is not { Kind: BankHoldKind.Deposit } held || held.Token != token)
                return BankOperationResult.Fail("That entry is not held by this deposit, so it was kept in the Bank.", BankErrorKind.PendingDeposit);

            return RemoveCore(id);
        }
    }

    private BankPendingWithdrawal Stamp(BankPendingWithdrawal pending, BankHoldKind kind) => pending with
    {
        OwnerProcessId = CurrentProcess.Id,
        OwnerProcessStartTicks = CurrentProcess.StartTicks,
        OwnerInstance = _instanceId,
        Kind = kind,
    };

    public int AddBox()
    {
        lock (_gate)
        {
            using var fileLock = BeginWrite(out var blocked);
            if (blocked is not null)
                return _boxCount - 1; // nothing changed; caller sees the (unchanged) last index

            var newCount = _boxCount + 1;
            var result = Commit(() => _boxCount = newCount);
            return result.Success ? newCount - 1 : _boxCount - 1;
        }
    }

    public string GetBoxName(int box)
    {
        lock (_gate)
            EnsureCurrent();
        var name = _boxNames.Get(box);
        return string.IsNullOrWhiteSpace(name) ? BoxDetailNameExtensions.GetDefaultBoxName(box) : name;
    }

    public void SetBoxName(int box, string name)
    {
        lock (_gate)
            EnsureCurrent();
        if (string.IsNullOrWhiteSpace(name))
            _boxNames.Remove(box);
        else
            _boxNames.Set(box, name.Trim());
    }

    public BankWallpaper GetBoxWallpaper(int box)
    {
        lock (_gate)
            EnsureCurrent();
        return _boxWallpapers.TryGet(box, out var w) ? w : BankWallpaper.Default;
    }

    public void SetBoxWallpaper(int box, BankWallpaper wallpaper)
    {
        lock (_gate)
            EnsureCurrent();
        if (wallpaper.Kind == BankWallpaperKind.Default)
            _boxWallpapers.Remove(box);
        else
            _boxWallpapers.Set(box, wallpaper);
    }

    public void Reload()
    {
        lock (_gate)
        {
            EnsureDirectory();
            _indexUnreadable = false;
            _warnings.Clear();
            _entries.Clear();
            Load();
            _loaded = true;
            _boxNames.Reload();
            _boxWallpapers.Reload();
        }
    }

    public BankOperationResult RestoreIndexFromBackup(byte[] rawIndexJson)
    {
        ArgumentNullException.ThrowIfNull(rawIndexJson);
        lock (_gate)
        {
            EnsureLoaded();

            if (!TryParse(rawIndexJson, out var parsed, out var parseError))
                return BankOperationResult.Fail($"That backup's contents could not be parsed as a Bank index: {parseError}", BankErrorKind.InvalidData);

            using var fileLock = TryAcquireLock();
            if (fileLock is null)
                return Busy();

            // 1) Keep a copy of what is being replaced, so the restore itself can be undone.
            if (!TryPreserveCurrentIndex(out var preserveError))
                return BankOperationResult.Fail($"The current Bank index could not be backed up first, so nothing was restored: {preserveError}");

            // 2) Swap in the restored entries (dropping any withdrawal holds — those belonged to a
            //    session that is not this one) and write atomically through the normal path.
            var restored = parsed.Entries.Select(e => e with { Pending = null }).ToList();
            var result = Commit(() =>
            {
                _entries.Clear();
                foreach (var entry in restored)
                    _entries[entry.Id] = entry;
                _boxCount = GetEffectiveBoxCount(parsed.BoxCount, restored);
            }, createBackup: false);
            if (!result.Success)
                return result;

            _indexUnreadable = false;
            _warnings.Clear();
            AddMissingDataWarning();
            return BankOperationResult.Succeeded;
        }
    }

    // --- internals -------------------------------------------------------

    private static BankOperationResult NotFound() => BankOperationResult.Fail("That entry no longer exists in the Bank.", BankErrorKind.NotFound);

    private static BankOperationResult PendingRefusal(BankEntry entry) => entry.IsPendingDeposit
        ? BankOperationResult.Fail("That entry was just deposited and is waiting for its save to be written.", BankErrorKind.PendingDeposit)
        : BankOperationResult.Fail("That entry is waiting for a withdrawal into an open save to be saved.", BankErrorKind.PendingWithdrawal);

    private static BankOperationResult Busy() =>
        BankOperationResult.Fail("Another app window is writing to the Bank right now. Try again in a moment.", BankErrorKind.Busy);

    private static BankOperationResult ReadOnlyRefusal() =>
        BankOperationResult.Fail("The Bank index could not be read safely, so it is open read-only. Nothing was changed.", BankErrorKind.ReadOnly);

    /// <summary>
    /// Shared prologue of every mutation (caller holds <see cref="_gate"/>): ensures the Bank is
    /// loaded, takes the cross-instance file lock, re-syncs from disk if another instance changed the
    /// index, and refuses writes while read-only. Returns the lock to dispose, or sets
    /// <paramref name="blocked"/> to the refusal.
    /// </summary>
    private IDisposable? BeginWrite(out BankOperationResult? blocked)
    {
        EnsureLoaded();
        var fileLock = TryAcquireLock();
        if (fileLock is null)
        {
            blocked = Busy();
            return null;
        }

        SyncFromDisk(full: true);
        if (_indexUnreadable)
        {
            fileLock.Dispose();
            blocked = ReadOnlyRefusal();
            return null;
        }

        blocked = null;
        return fileLock;
    }

    private FileStream? TryAcquireLock()
    {
        EnsureDirectory();
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(25); // another instance is mid-write; its critical section is a few file ops
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }

    private void EnsureDirectory()
    {
        try { Directory.CreateDirectory(_root); }
        catch { /* surfaced by the subsequent file operation */ }
    }

    /// <summary>Loads on first use, then picks up any external change (cheap timestamp check).</summary>
    private void EnsureCurrent()
    {
        if (!_loaded)
        {
            EnsureLoaded();
            return;
        }
        SyncFromDisk(full: false);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        EnsureDirectory();
        Load();
        _loaded = true;
        _boxNames.Reload();
        _boxWallpapers.Reload();
        ReleaseAbandonedHolds();
    }

    /// <summary>
    /// A hold whose owning app run is gone (closed or crashed before its save was written) can never
    /// complete, so it is cleared and the Pokémon simply stays in the Bank. Holds owned by another
    /// still-running app instance are left alone (they still block a double withdrawal).
    /// </summary>
    private void ReleaseAbandonedHolds()
    {
        var abandoned = _entries.Values.Where(e => e.Pending is { } p && !IsOwnerAlive(p)).ToList();
        if (abandoned.Count == 0 || _indexUnreadable)
            return;

        using var fileLock = TryAcquireLock();
        if (fileLock is null)
            return; // try again next run; holds are harmless while waiting
        SyncFromDisk(full: true);
        abandoned = _entries.Values.Where(e => e.Pending is { } p && !IsOwnerAlive(p)).ToList();
        if (abandoned.Count == 0)
            return;

        var result = Commit(() =>
        {
            foreach (var entry in abandoned)
                _entries[entry.Id] = entry with { Pending = null };
        }, createBackup: false);

        if (!result.Success)
            return;
        var deposits = abandoned.Count(e => e.IsPendingDeposit);
        var withdrawals = abandoned.Count - deposits;
        if (withdrawals > 0)
            _warnings.Add($"{withdrawals} withdrawal(s) from a previous session were never saved, so those Pokémon were kept in the Bank.");
        if (deposits > 0)
            _warnings.Add($"{deposits} deposit(s) from a previous session could not be confirmed, so those Pokémon were kept in the Bank. Check their save for duplicates.");
    }

    private bool IsOwnerAlive(BankPendingWithdrawal pending)
    {
        if (pending.OwnerInstance == _instanceId)
            return true;
        if (pending.OwnerProcessId == 0)
            return false;
        if (pending.OwnerProcessId == CurrentProcess.Id && pending.OwnerProcessStartTicks == CurrentProcess.StartTicks)
            return false; // an earlier Bank instance of this same run — it no longer tracks the hold

        try
        {
            using var process = Process.GetProcessById(pending.OwnerProcessId);
            if (process.HasExited)
                return false;
            return pending.OwnerProcessStartTicks == 0
                || process.StartTime.ToUniversalTime().Ticks == pending.OwnerProcessStartTicks;
        }
        catch
        {
            return false;
        }
    }

    private static (int Id, long StartTicks) GetCurrentProcessIdentity()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            long ticks;
            try { ticks = process.StartTime.ToUniversalTime().Ticks; }
            catch { ticks = 0; }
            return (process.Id, ticks);
        }
        catch
        {
            return (Environment.ProcessId, 0);
        }
    }

    private bool IsOccupied(int box, int slot) => _entries.Values.Any(e => e.Box == box && e.Slot == slot);

    private bool TryFindFirstEmpty(out int box, out int slot)
    {
        for (var b = 0; b < _boxCount; b++)
        {
            for (var s = 0; s < SlotsPerBox; s++)
            {
                if (!IsOccupied(b, s))
                {
                    box = b;
                    slot = s;
                    return true;
                }
            }
        }
        box = 0;
        slot = 0;
        return false;
    }

    private string GetEntityPath(Guid id) => Path.Combine(_root, $"{id:N}.bin");

    private BankOperationResult RemoveCore(Guid id)
    {
        var result = Commit(() => _entries.Remove(id));
        if (result.Success)
            TryDeleteFile(GetEntityPath(id)); // only after the index no longer references it
        return result;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* orphaned blob is harmless; the index is the source of truth for presence */ }
    }

    private static void TryWriteFile(string path, byte[] data)
    {
        try { File.WriteAllBytes(path, data); }
        catch { /* best-effort rollback */ }
    }

    /// <summary>
    /// Snapshots the in-memory index, runs <paramref name="mutate"/>, then persists. Any exception
    /// (including a failed <see cref="SaveIndex"/>) rolls the in-memory state back to the snapshot,
    /// so a failed write can never leave memory and disk disagreeing.
    /// </summary>
    private BankOperationResult Commit(Action mutate, bool createBackup = true)
    {
        var entriesSnapshot = new Dictionary<Guid, BankEntry>(_entries);
        var boxCountSnapshot = _boxCount;
        try
        {
            mutate();
            SaveIndex(createBackup);
            return BankOperationResult.Succeeded;
        }
        catch (Exception ex)
        {
            _entries.Clear();
            foreach (var (key, value) in entriesSnapshot)
                _entries[key] = value;
            _boxCount = boxCountSnapshot;
            return BankOperationResult.Fail(ex.Message);
        }
    }

    private void SaveIndex(bool createBackup)
    {
        var envelope = new BankIndexFile(SchemaVersion, _boxCount, _entries.Values.OrderBy(e => e.Box).ThenBy(e => e.Slot).ToList());
        var json = JsonSerializer.Serialize(envelope, IndexOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tmpPath = _indexPath + ".tmp";
        File.WriteAllBytes(tmpPath, bytes);

        if (File.Exists(_indexPath))
        {
            try { File.Copy(_indexPath, _indexPath + BackupSuffix, overwrite: true); }
            catch { /* best-effort — the tmp file is still the source of truth for this write */ }
        }

        File.Move(tmpPath, _indexPath, overwrite: true);
        _lastIndexHash = Hash(bytes);
        _lastStamp = GetStamp();

        // Backup Manager (#135) integration: the Bank's index rides the same timestamped
        // backup/retention/restore mechanism a real save file gets, rather than inventing a second one.
        if (!createBackup)
            return;
        try { _backupService?.CreateBackup(BackupIdentity, bytes, _backupRetention); }
        catch { /* never let the backup side-effect fail the primary write */ }
    }

    /// <summary>
    /// Copies the index about to be replaced by a restore into the Backup Manager (or, without one, a
    /// local <c>index.json.pre-restore</c> file). Nothing to preserve for a brand-new Bank.
    /// </summary>
    private bool TryPreserveCurrentIndex(out string? error)
    {
        error = null;
        byte[] current;
        try
        {
            current = File.Exists(_indexPath)
                ? File.ReadAllBytes(_indexPath)
                : Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (current.Length == 0)
            return true;

        try
        {
            if (_backupService is not null)
            {
                if (_backupService.CreateBackup(BackupIdentity, current, _backupRetention) is not null)
                    return true;
            }

            var preRestore = _indexPath + PreRestoreSuffix;
            var tmp = preRestore + ".tmp";
            File.WriteAllBytes(tmp, current);
            File.Move(tmp, preRestore, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Re-reads the index if another instance changed it. <paramref name="full"/> compares content
    /// (used under the write lock); otherwise only the file's timestamp/length is checked.
    /// </summary>
    private void SyncFromDisk(bool full)
    {
        var stamp = GetStamp();
        if (stamp is null)
            return; // no index on disk (new Bank, or deleted externally): keep what is in memory
        if (!full && stamp == _lastStamp)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_indexPath);
        }
        catch
        {
            return;
        }

        _lastStamp = stamp;
        var hash = Hash(bytes);
        if (hash == _lastIndexHash)
            return;
        _lastIndexHash = hash;

        if (TryParse(bytes, out var file, out var error))
        {
            Apply(file);
            if (_indexUnreadable)
            {
                _indexUnreadable = false;
                _warnings.Add("The Bank index became readable again and was reloaded.");
            }
            AddMissingDataWarning();
            return;
        }

        // Another writer left something unparseable: never overwrite it from a stale copy.
        if (!_indexUnreadable)
        {
            _indexUnreadable = true;
            _warnings.Add($"The Bank index was changed outside this window and could not be read ({error}). The Bank is read-only until it is fixed.");
        }
    }

    private (DateTime, long)? GetStamp()
    {
        try
        {
            var info = new FileInfo(_indexPath);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private void Load()
    {
        _lastStamp = GetStamp();
        if (_lastStamp is null)
        {
            _boxCount = 1;
            _lastIndexHash = null;
            return; // brand-new Bank — not corruption.
        }

        byte[] primaryBytes;
        try
        {
            primaryBytes = File.ReadAllBytes(_indexPath);
        }
        catch (Exception ex)
        {
            primaryBytes = [];
            _warnings.Add($"The Bank index could not be read ({ex.Message}).");
        }

        // Remember the primary's content even if it is corrupt, so a later sync only reacts to a
        // genuinely new external write (a recovered-from-backup Bank must stay writable).
        _lastIndexHash = Hash(primaryBytes);

        if (TryParse(primaryBytes, out var primary, out var primaryError))
        {
            Apply(primary);
            AddMissingDataWarning();
            return;
        }

        _warnings.Add($"The primary Bank index could not be parsed ({primaryError}); trying its backup copy.");

        var bakPath = _indexPath + BackupSuffix;
        if (File.Exists(bakPath))
        {
            byte[] backupBytes;
            try { backupBytes = File.ReadAllBytes(bakPath); }
            catch { backupBytes = []; }

            if (TryParse(backupBytes, out var backup, out var backupError))
            {
                _warnings.Add("The Bank index was recovered from its backup copy. The corrupt primary file will be replaced the next time the Bank is written to.");
                Apply(backup);
                AddMissingDataWarning();
                return;
            }
            _warnings.Add($"The backup Bank index could not be parsed either ({backupError}).");
        }
        else
        {
            _warnings.Add("No backup Bank index was available to recover from.");
        }

        _indexUnreadable = true;
        _boxCount = Math.Max(_boxCount, 1);
        _warnings.Add("Opening the Bank read-only to avoid data loss. Existing entity files on disk were left untouched.");
    }

    private static bool TryParse(byte[] bytes, out BankIndexFile file, out string? error)
    {
        try
        {
            var parsed = bytes.Length == 0 ? null : JsonSerializer.Deserialize<BankIndexFile>(bytes);
            if (parsed?.Entries is null)
            {
                file = new BankIndexFile(SchemaVersion, 1, []);
                error = "empty or null document";
                return false;
            }
            if (parsed.Entries.Any(e => e is null || e.Info is null || e.Id == Guid.Empty))
            {
                file = new BankIndexFile(SchemaVersion, 1, []);
                error = "the document contains malformed entries";
                return false;
            }
            file = parsed;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            file = new BankIndexFile(SchemaVersion, 1, []);
            error = ex.Message;
            return false;
        }
    }

    private void Apply(BankIndexFile file)
    {
        _entries.Clear();
        foreach (var entry in file.Entries)
            _entries[entry.Id] = entry;
        _boxCount = GetEffectiveBoxCount(file.BoxCount, file.Entries);
    }

    private static int GetEffectiveBoxCount(int declared, IEnumerable<BankEntry> entries)
    {
        var needed = entries.Select(e => e.Box + 1).DefaultIfEmpty(1).Max();
        return Math.Max(1, Math.Max(declared, needed));
    }

    private void AddMissingDataWarning()
    {
        var missing = _entries.Keys.Count(id => !File.Exists(GetEntityPath(id)));
        if (missing > 0)
            _warnings.Add($"{missing} Bank entr{(missing == 1 ? "y references a data file" : "ies reference data files")} missing on disk. They are shown but cannot be withdrawn or exported.");
    }
}
