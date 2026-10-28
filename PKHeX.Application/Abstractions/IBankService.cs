namespace PKHeX.Application.Abstractions;

/// <summary>
/// Fixed identifiers the Bank uses when it reuses other Application ports under its own identity
/// rather than one derived from a save file path (the Bank is not a save file).
/// </summary>
public static class BankConstants
{
    /// <summary>The <see cref="ISaveBackupService"/> identity the Bank's index backups are grouped under.</summary>
    public const string BackupIdentity = "bank";
}

/// <summary>
/// How a Bank box is decorated. Mirrors the native in-game box wallpaper concept
/// (<see cref="object"/>-free so Application stays framework-free), but the Bank is not tied to any
/// one save's generation, so a wallpaper is either a flat palette color or a reference to one of the
/// bundled native box-art assets (e.g. <c>"swsh:box_wp01swsh"</c>) resolved by the host layer.
/// </summary>
public enum BankWallpaperKind
{
    /// <summary>No explicit wallpaper chosen — the UI shows its own default.</summary>
    Default = 0,

    /// <summary>A flat palette color, identified by a small integer index into a UI-defined palette.</summary>
    Color = 1,

    /// <summary>A bundled native box-art asset, identified by a host-defined asset id.</summary>
    Art = 2,
}

/// <summary>
/// A Bank box's decoration. This is <b>decoration, never data</b>: losing it (a missing/corrupt side
/// file) must never affect which Pokémon are in the Bank or where — see <see cref="IBankService"/>.
/// </summary>
public readonly record struct BankWallpaper(BankWallpaperKind Kind, string Value)
{
    public static readonly BankWallpaper Default = new(BankWallpaperKind.Default, string.Empty);

    public static BankWallpaper Color(int paletteIndex) => new(BankWallpaperKind.Color, paletteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static BankWallpaper Art(string assetId) => new(BankWallpaperKind.Art, assetId);
}

/// <summary>
/// Lightweight, pre-computed display metadata captured at deposit time so the Bank's box grid can be
/// rendered without re-parsing every stored entity's raw bytes. <see cref="Format"/> is the stored
/// entity's own file extension (e.g. <c>".pk9"</c>) — raw bytes alone are ambiguous between same-size
/// native structs (e.g. PK8 vs PB8), so this is what lets a withdrawal reconstruct the exact original
/// type instead of guessing from size/heuristics.
/// </summary>
public sealed record BankEntryInfo(
    ushort Species,
    byte Form,
    bool IsShiny,
    string Nickname,
    byte Level,
    byte Generation,
    string Format,
    int HeldItem,
    bool IsEgg,
    string SourceLabel);

/// <summary>Which direction a <see cref="BankPendingWithdrawal"/> hold is waiting to finish.</summary>
public enum BankHoldKind
{
    /// <summary>The entry's Pokémon was written into an open save; the entry leaves the Bank once that save is written.</summary>
    Withdrawal = 0,

    /// <summary>
    /// The entry was just deposited from an open save; it stays in the Bank once that save is written
    /// with the slot cleared, and is removed again if the save is closed without saving.
    /// </summary>
    Deposit = 1,
}

/// <summary>
/// A "hold until saved" marker on a Bank entry whose converted copy has been written into an open
/// save's in-memory data but not yet to disk. A <see cref="BankHoldKind.Deposit"/> hold is the mirror
/// image: the entry was added from an open save whose cleared slot is not on disk yet. While present the entry stays in the Bank (its raw
/// bytes are never deleted) and cannot be withdrawn a second time; it is removed from the Bank only
/// once that save is successfully written to disk with the withdrawn Pokémon still in it, and the
/// marker is simply cleared (the entry stays in the Bank) if the save is closed/switched/discarded
/// without saving or the app exits. Persisted in the index so it is visible across restarts and to
/// other app instances sharing the same Bank.
/// </summary>
/// <param name="Token">Random id of this particular hold; completing/releasing requires the matching token.</param>
/// <param name="SaveLabel">Human-readable description of the target save (game/trainer), for display.</param>
/// <param name="SavePath">Path of the target save when known (informational only).</param>
/// <param name="Box">Target box in the save the Pokémon was written into.</param>
/// <param name="Slot">Target slot in that box.</param>
/// <param name="StartedUtc">When the withdrawal was started.</param>
/// <param name="OwnerProcessId">Process that holds the marker. Stamped by the Bank service, not the caller.</param>
/// <param name="OwnerProcessStartTicks">Start time (UTC ticks) of the owning process, so a reused process id is not mistaken for a live owner. Stamped by the Bank service.</param>
/// <param name="OwnerInstance">Identity of the Bank service instance (one per app run) that holds the marker. Stamped by the Bank service.</param>
/// <param name="Kind">Withdrawal or deposit. Indexes written before deposits were held read back as <see cref="BankHoldKind.Withdrawal"/>.</param>
/// <remarks>
/// An abandoned hold of either kind (its app run ended before the save was written) is cleared and
/// the entry kept: a duplicate is acceptable, a loss is not.
/// </remarks>
public sealed record BankPendingWithdrawal(
    Guid Token,
    string SaveLabel,
    string? SavePath,
    int Box,
    int Slot,
    DateTimeOffset StartedUtc,
    int OwnerProcessId = 0,
    long OwnerProcessStartTicks = 0,
    Guid OwnerInstance = default,
    BankHoldKind Kind = BankHoldKind.Withdrawal);

/// <summary>
/// One Pokémon stored in the Bank: a stable id (used to find its raw-bytes file on disk), where it
/// currently sits in the Bank's own box layout, its display metadata, when it was added, and — while
/// a withdrawal into an open save is waiting for that save to be written — its
/// <see cref="BankPendingWithdrawal"/> hold.
/// </summary>
public sealed record BankEntry(
    Guid Id,
    int Box,
    int Slot,
    BankEntryInfo Info,
    DateTimeOffset AddedUtc,
    BankPendingWithdrawal? Pending = null)
{
    public bool IsPendingWithdrawal => Pending is { Kind: BankHoldKind.Withdrawal };
    public bool IsPendingDeposit => Pending is { Kind: BankHoldKind.Deposit };
}

/// <summary>
/// Why a Bank operation failed, so the UI can show a localized explanation (the
/// <see cref="BankOperationResult.Error"/> text is a technical English detail for logs/diagnostics).
/// </summary>
public enum BankErrorKind
{
    None = 0,
    /// <summary>The index could not be read safely, so the Bank is open read-only.</summary>
    ReadOnly,
    /// <summary>The entry no longer exists in the Bank.</summary>
    NotFound,
    /// <summary>The box/slot is out of range.</summary>
    InvalidSlot,
    /// <summary>The destination slot is already occupied.</summary>
    Occupied,
    /// <summary>The entry is held by a pending withdrawal (or not held by the expected one).</summary>
    PendingWithdrawal,
    /// <summary>Another app window is writing to the Bank right now.</summary>
    Busy,
    /// <summary>The supplied data is not a valid Bank index.</summary>
    InvalidData,
    /// <summary>A file could not be read or written.</summary>
    Io,
    /// <summary>The entry was just deposited and is waiting for its save to be written.</summary>
    PendingDeposit,
}

/// <summary>Result of an operation that creates a new <see cref="BankEntry"/>.</summary>
public readonly record struct BankAddResult(bool Success, BankEntry? Entry, string? Error, BankErrorKind Kind = BankErrorKind.None)
{
    public static BankAddResult Fail(string error, BankErrorKind kind = BankErrorKind.Io) => new(false, null, error, kind);
    public static BankAddResult Ok(BankEntry entry) => new(true, entry, null);
}

/// <summary>Result of an operation on an existing <see cref="BankEntry"/> that doesn't return one.</summary>
public readonly record struct BankOperationResult(bool Success, string? Error, BankErrorKind Kind = BankErrorKind.None)
{
    public static readonly BankOperationResult Succeeded = new(true, null);
    public static BankOperationResult Fail(string error, BankErrorKind kind = BankErrorKind.Io) => new(false, error, kind);
}

/// <summary>
/// Port for a persistent, save-independent vault of Pokémon ("the Bank") that survives app restarts
/// and is not tied to any single open save. Implemented in the Infrastructure layer as a flat-file
/// store: one raw-bytes file per entity (native format, exactly as deposited — never normalized to a
/// universal representation) plus one small index of <see cref="BankEntry"/> records. Cross-generation
/// movement is intentionally not this type's job — callers run the raw bytes this returns through
/// <c>PKHeX.Core</c>'s own conversion pipeline (<c>SaveFile.GetCompatible</c>/<c>EntityConverter</c>) at
/// the moment of withdrawal, exactly as the existing cross-window drag-and-drop feature already does.
/// </summary>
public interface IBankService
{
    /// <summary>Fixed number of slots in every Bank box.</summary>
    int SlotsPerBox { get; }

    /// <summary>Current number of boxes. Grows automatically as needed; never shrinks on its own.</summary>
    int BoxCount { get; }

    /// <summary>
    /// True if the on-disk index could not be safely read (both the primary file and its backup
    /// failed to parse). While true, every mutating member fails with an explanatory error instead
    /// of risking data loss by resetting to an empty index.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>Human-readable notes recorded while loading (e.g. "recovered from backup"). Never throws.</summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>All entries across every box, ordered by box then slot. Snapshot — safe to enumerate freely.</summary>
    IReadOnlyList<BankEntry> GetAll();

    /// <summary>Entries in a single box, ordered by slot. Empty if the box has no occupants (not an error).</summary>
    IReadOnlyList<BankEntry> GetBox(int box);

    /// <summary>Looks up one entry by id, or <see langword="null"/> if it doesn't exist.</summary>
    BankEntry? Find(Guid id);

    /// <summary>Reads the stored raw bytes for an entry, or <see langword="null"/> if missing/unreadable.</summary>
    byte[]? GetRawData(Guid id);

    /// <summary>
    /// Deposits a new entity. Placed in the given <paramref name="box"/>/<paramref name="slot"/> if
    /// both are supplied (the slot must be empty), otherwise placed in the first empty slot —
    /// growing the Bank with a new box via <see cref="AddBox"/> first if every existing box is full.
    /// </summary>
    BankAddResult Add(byte[] rawData, BankEntryInfo info, int? box = null, int? slot = null);

    /// <summary>
    /// Moves an existing entry to a new box/slot. Fails if the destination is occupied unless
    /// <paramref name="swap"/> is true, in which case the two entries trade places.
    /// </summary>
    BankOperationResult Move(Guid id, int box, int slot, bool swap = false);

    /// <summary>Replaces an existing entry's raw bytes and display metadata in place (same box/slot).</summary>
    BankOperationResult Replace(Guid id, byte[] rawData, BankEntryInfo info);

    /// <summary>
    /// Removes an entry and deletes its raw-bytes file. Refused while the entry has a
    /// <see cref="BankPendingWithdrawal"/> hold — a held entry leaves the Bank only through
    /// <see cref="CompletePendingWithdrawal"/>.
    /// </summary>
    BankOperationResult Remove(Guid id);

    /// <summary>
    /// Places a "hold until saved" marker on an entry that is about to be written into an open save.
    /// Fails if the entry is missing or already held (blocks withdrawing the same Pokémon twice).
    /// Never deletes anything.
    /// </summary>
    BankOperationResult MarkPendingWithdrawal(Guid id, BankPendingWithdrawal pending);

    /// <summary>
    /// Clears a hold whose <see cref="BankPendingWithdrawal.Token"/> matches, leaving the entry (and its
    /// raw bytes) in the Bank — used when the target save is closed/discarded without being written, the
    /// slot write is undone, or the write itself failed. A no-op success if the hold is already gone.
    /// </summary>
    BankOperationResult ReleasePendingWithdrawal(Guid id, Guid token);

    /// <summary>
    /// Finishes a withdrawal after the target save was successfully written to disk with the Pokémon
    /// in it: removes the entry and only then deletes its raw bytes. Refused (entry kept) unless the
    /// entry still carries the hold identified by <paramref name="token"/>.
    /// </summary>
    BankOperationResult CompletePendingWithdrawal(Guid id, Guid token);

    /// <summary>
    /// Adds an entry deposited from an open save, already carrying a <see cref="BankHoldKind.Deposit"/>
    /// hold (the service stamps the owner and forces the kind). Same placement rules as <see cref="Add"/>.
    /// </summary>
    BankAddResult AddPendingDeposit(byte[] rawData, BankEntryInfo info, BankPendingWithdrawal hold);

    /// <summary>
    /// The deposit's save was written with the slot cleared: clears the hold and keeps the entry. A
    /// no-op success if the hold is already gone.
    /// </summary>
    BankOperationResult ConfirmPendingDeposit(Guid id, Guid token);

    /// <summary>
    /// The deposit's save was closed without being written (or the slot could not be cleared): removes
    /// the entry and its raw bytes. Refused (entry kept) unless the entry still carries the deposit hold
    /// identified by <paramref name="token"/>.
    /// </summary>
    BankOperationResult CancelPendingDeposit(Guid id, Guid token);

    /// <summary>Appends a new, empty box. Returns its index.</summary>
    int AddBox();

    /// <summary>Box display name, or a default (e.g. "Box 1") if none was ever set.</summary>
    string GetBoxName(int box);

    /// <summary>Sets a box's display name. Best-effort decoration — failures never affect entries.</summary>
    void SetBoxName(int box, string name);

    /// <summary>Box wallpaper, or <see cref="BankWallpaper.Default"/> if none was ever set.</summary>
    BankWallpaper GetBoxWallpaper(int box);

    /// <summary>Sets a box's wallpaper. Best-effort decoration — failures never affect entries.</summary>
    void SetBoxWallpaper(int box, BankWallpaper wallpaper);

    /// <summary>
    /// Re-reads the index from disk (e.g. after an external restore via the Backup Manager). If the
    /// Bank was previously read-only and the file now parses, mutation resumes being allowed.
    /// </summary>
    void Reload();

    /// <summary>
    /// Replaces the on-disk index with a previously-backed-up copy of its raw JSON bytes (from
    /// <see cref="ISaveBackupService"/>, keyed by the Bank's own fixed backup identity), then
    /// reloads. Validates the bytes parse before touching anything on disk — a corrupt backup is
    /// refused rather than risking a worse state than whatever is currently loaded. The current index
    /// is itself backed up first (so a restore can be undone by restoring that copy), the replacement
    /// is written atomically, any withdrawal holds in the restored copy are dropped, and entries that
    /// reference raw-bytes files no longer on disk are reported through <see cref="Warnings"/> rather
    /// than failing the restore.
    /// </summary>
    BankOperationResult RestoreIndexFromBackup(byte[] rawIndexJson);

    /// <summary>
    /// Ids of entries whose raw-bytes file is missing on disk (e.g. after restoring an older index).
    /// Such entries are listed but cannot be withdrawn or exported.
    /// </summary>
    IReadOnlyCollection<Guid> GetEntriesWithMissingData();
}
