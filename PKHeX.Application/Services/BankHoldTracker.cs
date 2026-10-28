using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>How a held withdrawal was settled when its save was written.</summary>
public enum BankHoldResolution
{
    /// <summary>The written save contains the Pokémon, so it was removed from the Bank.</summary>
    RemovedFromBank,

    /// <summary>The written save no longer contains the withdrawn Pokémon (moved out, edited, undone or cleared), so it stayed in the Bank.</summary>
    KeptInBank,

    /// <summary>The Bank refused to finish the withdrawal (e.g. it is read-only or the hold changed), so it stayed in the Bank.</summary>
    BankRefused,

    /// <summary>The deposit's save was written with the slot cleared, so the entry now stays in the Bank.</summary>
    DepositCompleted,

    /// <summary>The deposit's save was closed without being written, so the entry was removed from the Bank again.</summary>
    DepositCancelled,

    /// <summary>The Bank could not settle the deposit; the entry is kept (with its hold until the next app run).</summary>
    DepositKept,
}

/// <summary>Outcome for one held withdrawal, reported after a save write.</summary>
public sealed record BankHoldOutcome(Guid EntryId, BankEntryInfo? Info, BankHoldResolution Resolution, string? Detail);

/// <summary>
/// Implements the Bank's <b>hold until saved</b> withdrawal contract. Withdrawing writes the converted
/// Pokémon into an open save's in-memory data only; the Bank entry is merely <i>held</i>
/// (<see cref="IBankService.MarkPendingWithdrawal"/>, persisted) and its raw bytes stay on disk. The
/// entry leaves the Bank only when that same save session is successfully written to disk
/// (<see cref="OnSaveWritten"/>, driven by <see cref="ISaveFileGateway.SaveWritten"/>) <b>and the
/// written snapshot still contains the exact bytes that were withdrawn</b>. Every other ending —
/// the save is closed, switched, reloaded or discarded without being written, or the app exits —
/// releases the hold and the Pokémon simply stays in the Bank.
/// </summary>
/// <remarks>
/// <para>Edge-case policy (documented choice): the worst case must always be a duplicate, never a loss.</para>
/// <list type="bullet">
/// <item>The written snapshot (not the live save, which may have been edited during the asynchronous
/// write) is searched for the withdrawn bytes: the target slot first, then every box and party slot,
/// so moving the Pokémon to another slot before saving still completes the withdrawal.</item>
/// <item>If the Pokémon was undone, cleared, overwritten or edited before saving, its exact bytes are
/// not in the written file, so it is kept in the Bank (an edited copy in the save is then a benign
/// duplicate). Undo does not release the hold immediately (a later Redo would otherwise put an
/// unheld copy back); instead a hold whose Pokémon is no longer in the live save is released lazily
/// when the user tries to withdraw that entry again (<see cref="TryReleaseIfAbsent"/>).</item>
/// <item>A save written after the user switched away belongs to a session whose holds were already
/// released, so it removes nothing (benign duplicate).</item>
/// </list>
/// <para>
/// Deposits are held the same way in the other direction (<see cref="Deposit"/>): the entry is added to
/// the Bank with a <see cref="BankHoldKind.Deposit"/> hold while the cleared slot exists only in memory.
/// Writing that save confirms it (the entry stays); closing, switching or discarding the save without
/// writing it cancels it (the entry is removed; the Pokémon is still in the file on disk). A written
/// snapshot that still holds the deposited bytes at their source (the write began before the deposit)
/// leaves the hold in place for the next write. If the Bank cannot settle a deposit, the entry is kept:
/// an abandoned hold is cleared on the next run, so the worst case stays a duplicate.
/// </para>
/// <para>
/// A session can end while one of its writes is still running (the file is closed mid-save). Its
/// deposits are then not cancelled yet: they wait for that write (<see cref="OnSaveWriteStarted"/> /
/// <see cref="OnSaveWritten"/> / <see cref="OnSaveWriteFailed"/>), and whatever reached the disk
/// decides. If the app exits first, the persisted hold is kept on the next run.
/// </para>
/// <para>Thread affinity: call on the UI thread (same contract as <see cref="UndoRedoService"/>).</para>
/// </remarks>
public sealed class BankHoldTracker
{
    private sealed class Hold
    {
        public required Guid EntryId { get; init; }
        public required Guid Token { get; init; }
        public required SaveFile Save { get; init; }
        public required SlotLocation Target { get; init; }
        public BankHoldKind Kind { get; init; } = BankHoldKind.Withdrawal;
        public byte[]? WrittenData { get; set; }

        /// <summary>Deposit from the party: identical copies the party still held right after this one left it.</summary>
        public int RemainingInParty { get; init; }

        /// <summary>Deposit whose session ended while a write of it was still running.</summary>
        public bool SessionEnded { get; set; }
    }

    private readonly IBankService _bank;
    private readonly List<Hold> _holds = [];
    private readonly Dictionary<SaveFile, int> _writesInFlight = new(ReferenceEqualityComparer.Instance);

    private bool HasWriteInFlight(SaveFile save) => _writesInFlight.TryGetValue(save, out var count) && count > 0;

    private void EndWrite(SaveFile save)
    {
        if (!_writesInFlight.TryGetValue(save, out var count))
            return;
        if (count <= 1)
            _writesInFlight.Remove(save);
        else
            _writesInFlight[save] = count - 1;
    }

    /// <summary><see cref="ISaveFileGateway.SaveWriteStarted"/>: a snapshot of <paramref name="save"/> is being written.</summary>
    public void OnSaveWriteStarted(SaveFile save)
    {
        ArgumentNullException.ThrowIfNull(save);
        _writesInFlight[save] = (_writesInFlight.TryGetValue(save, out var count) ? count : 0) + 1;
    }

    /// <summary>
    /// <see cref="ISaveFileGateway.SaveWriteFailed"/>: nothing reached the disk. Deposits of an ended
    /// session that were only waiting for this write are cancelled now.
    /// </summary>
    public IReadOnlyList<BankHoldOutcome> OnSaveWriteFailed(SaveFile save)
    {
        ArgumentNullException.ThrowIfNull(save);
        EndWrite(save);
        if (HasWriteInFlight(save))
            return [];

        var outcomes = _holds.Where(h => h.SessionEnded && ReferenceEquals(h.Save, save)).ToList()
            .Select(CancelHeldDeposit).ToList();
        if (outcomes.Count > 0)
            HoldsChanged?.Invoke();
        return outcomes;
    }

    private BankHoldOutcome CancelHeldDeposit(Hold hold)
    {
        _holds.Remove(hold);
        var info = _bank.Find(hold.EntryId)?.Info;
        var result = _bank.CancelPendingDeposit(hold.EntryId, hold.Token);
        return result.Success
            ? new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.DepositCancelled, null)
            : new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.DepositKept, result.Error);
    }

    public BankHoldTracker(IBankService bank) => _bank = bank;

    /// <summary>Raised whenever a hold is added, settled or released, so Bank views can refresh.</summary>
    public event Action? HoldsChanged;

    /// <summary>Ids of entries this app session currently holds.</summary>
    public IReadOnlyCollection<Guid> HeldEntryIds => _holds.Select(h => h.EntryId).ToList();

    public bool IsHeldBySession(Guid entryId) => _holds.Any(h => h.EntryId == entryId);

    /// <summary>
    /// Step 1, before anything is written into the save: places the persisted hold. Fails (nothing
    /// written anywhere) if the entry is missing or already held.
    /// </summary>
    public BankOperationResult Reserve(Guid entryId, SaveFile save, SlotLocation target, string saveLabel, string? savePath, out Guid token)
    {
        ArgumentNullException.ThrowIfNull(save);
        token = Guid.NewGuid();
        var pending = new BankPendingWithdrawal(token, saveLabel, savePath, target.Box, target.Slot, DateTimeOffset.UtcNow);
        var result = _bank.MarkPendingWithdrawal(entryId, pending);
        if (!result.Success)
            return result;

        _holds.Add(new Hold { EntryId = entryId, Token = token, Save = save, Target = target });
        HoldsChanged?.Invoke();
        return result;
    }

    /// <summary>Step 2a: the slot write succeeded; remember exactly what the save now holds.</summary>
    public void Confirm(Guid entryId, Guid token, PKM written)
    {
        var hold = _holds.FirstOrDefault(h => h.EntryId == entryId && h.Token == token);
        if (hold is not null)
            hold.WrittenData = GetStoredData(written);
    }

    /// <summary>Step 2b: the slot write failed or was refused; drop the hold, the entry stays in the Bank.</summary>
    public void Abort(Guid entryId, Guid token)
    {
        _holds.RemoveAll(h => h.EntryId == entryId && h.Token == token);
        _bank.ReleasePendingWithdrawal(entryId, token);
        HoldsChanged?.Invoke();
    }

    /// <summary>
    /// Settles every hold belonging to <paramref name="original"/> after it was written to disk as
    /// <paramref name="written"/>: removes the entry from the Bank if the written data contains the
    /// withdrawn bytes, otherwise keeps it. Holds of other save sessions are untouched.
    /// </summary>
    public IReadOnlyList<BankHoldOutcome> OnSaveWritten(SaveFile original, SaveFile written)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(written);
        EndWrite(original);

        var settled = _holds.Where(h => ReferenceEquals(h.Save, original)).ToList();
        if (settled.Count == 0)
            return [];

        var outcomes = new List<BankHoldOutcome>(settled.Count);
        foreach (var hold in settled)
        {
            if (hold.Kind == BankHoldKind.Deposit)
            {
                if (SettleDepositOnWrite(hold, written) is { } outcome)
                    outcomes.Add(outcome);
                continue;
            }

            _holds.Remove(hold);
            var info = _bank.Find(hold.EntryId)?.Info;
            if (hold.WrittenData is not null && Contains(written, hold.Target, hold.WrittenData))
            {
                var result = _bank.CompletePendingWithdrawal(hold.EntryId, hold.Token);
                if (result.Success)
                {
                    outcomes.Add(new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.RemovedFromBank, null));
                    continue;
                }

                _bank.ReleasePendingWithdrawal(hold.EntryId, hold.Token);
                outcomes.Add(new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.BankRefused, result.Error));
                continue;
            }

            _bank.ReleasePendingWithdrawal(hold.EntryId, hold.Token);
            outcomes.Add(new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.KeptInBank, null));
        }

        HoldsChanged?.Invoke();
        return outcomes;
    }

    /// <summary>
    /// The active save changed (opened another, reloaded, restored a backup, closed or discarded):
    /// every hold that does not belong to <paramref name="current"/> can never be saved any more.
    /// Withdrawals are released (the Pokémon stays in the Bank); deposits are cancelled (the entry is
    /// removed, the Pokémon is still in that save's file). Returns the deposit outcomes.
    /// </summary>
    public IReadOnlyList<BankHoldOutcome> OnSaveSessionChanged(SaveFile? current)
    {
        var ended = _holds.Where(h => !ReferenceEquals(h.Save, current)).ToList();
        if (ended.Count == 0)
            return [];

        var outcomes = new List<BankHoldOutcome>();
        foreach (var hold in ended)
        {
            if (hold.Kind == BankHoldKind.Deposit)
            {
                if (HasWriteInFlight(hold.Save))
                    hold.SessionEnded = true; // the running write decides
                else
                    outcomes.Add(CancelHeldDeposit(hold));
                continue;
            }

            _holds.Remove(hold);
            _bank.ReleasePendingWithdrawal(hold.EntryId, hold.Token);
        }
        HoldsChanged?.Invoke();
        return outcomes;
    }

    /// <summary>Ends every hold (the app is shutting down without further saves).</summary>
    public IReadOnlyList<BankHoldOutcome> ReleaseAll() => OnSaveSessionChanged(null);

    /// <summary>
    /// Deposits <paramref name="pk"/> (read from <paramref name="source"/> in <paramref name="save"/>)
    /// into the Bank with a deposit hold. Call before clearing the slot; if the slot then cannot be
    /// cleared, call <see cref="CancelDeposit"/>.
    /// </summary>
    public BankAddResult Deposit(SaveFile save, PKM pk, SlotLocation source, string saveLabel, string? savePath)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(pk);

        var data = GetStoredData(pk);
        var token = Guid.NewGuid();
        var pending = new BankPendingWithdrawal(token, saveLabel, savePath, source.Box, source.Slot, DateTimeOffset.UtcNow, Kind: BankHoldKind.Deposit);
        var result = _bank.AddPendingDeposit(BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, saveLabel), pending);
        if (!result.Success || result.Entry is null)
            return result;

        _holds.Add(new Hold
        {
            EntryId = result.Entry.Id,
            Token = token,
            Save = save,
            Target = source,
            Kind = BankHoldKind.Deposit,
            WrittenData = data,
            RemainingInParty = source.IsParty ? CountInParty(save, data) - 1 : 0,
        });
        HoldsChanged?.Invoke();
        return result;
    }

    /// <summary>Undoes a <see cref="Deposit"/> whose slot could not be cleared: removes the entry again.</summary>
    public BankOperationResult CancelDeposit(Guid entryId)
    {
        var hold = _holds.FirstOrDefault(h => h.EntryId == entryId && h.Kind == BankHoldKind.Deposit);
        if (hold is null)
            return BankOperationResult.Succeeded;

        _holds.Remove(hold);
        var result = _bank.CancelPendingDeposit(hold.EntryId, hold.Token);
        HoldsChanged?.Invoke();
        return result;
    }

    /// <summary>Whether <paramref name="entryId"/> is a deposit this session is still waiting to save.</summary>
    public bool IsPendingDeposit(Guid entryId) => _holds.Any(h => h.EntryId == entryId && h.Kind == BankHoldKind.Deposit);

    private BankHoldOutcome? SettleDepositOnWrite(Hold hold, SaveFile written)
    {
        var data = hold.WrittenData!;
        var stillThere = hold.Target.IsParty
            ? CountInParty(written, data) > hold.RemainingInParty
            : Matches(TryRead(written, hold.Target), data);
        if (stillThere)
        {
            // This write began before the deposit: the file still has the Pokémon. Keep waiting for a
            // later write, unless the session is over and no further write is coming.
            return hold.SessionEnded && !HasWriteInFlight(hold.Save) ? CancelHeldDeposit(hold) : null;
        }

        _holds.Remove(hold);
        var info = _bank.Find(hold.EntryId)?.Info;
        var result = _bank.ConfirmPendingDeposit(hold.EntryId, hold.Token);
        return result.Success
            ? new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.DepositCompleted, null)
            : new BankHoldOutcome(hold.EntryId, info, BankHoldResolution.DepositKept, result.Error);
    }

    private static int CountInParty(SaveFile sav, byte[] expected)
    {
        if (!sav.HasParty)
            return 0;
        var count = 0;
        for (var slot = 0; slot < sav.PartyCount; slot++)
        {
            if (Matches(TryRead(sav, SlotLocation.FromParty(slot)), expected))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Forgets every hold without touching the Bank — used after the Bank index was replaced by a
    /// restore, which already dropped all holds on disk.
    /// </summary>
    public void Forget()
    {
        if (_holds.Count == 0)
            return;
        _holds.Clear();
        HoldsChanged?.Invoke();
    }

    /// <summary>
    /// If this session holds <paramref name="entryId"/> but the live save no longer contains the
    /// withdrawn Pokémon (the write was undone, or the slot was cleared/overwritten), releases the
    /// hold so the entry can be withdrawn again. Returns true when the entry is (now) not held by
    /// this session.
    /// </summary>
    public bool TryReleaseIfAbsent(Guid entryId, SaveFile live)
    {
        var hold = _holds.FirstOrDefault(h => h.EntryId == entryId);
        if (hold is null)
            return true;
        if (hold.Kind == BankHoldKind.Deposit)
            return false; // a deposit settles only through a save write or the end of its session
        if (ReferenceEquals(hold.Save, live) && hold.WrittenData is not null && Contains(live, hold.Target, hold.WrittenData))
            return false;

        _holds.Remove(hold);
        _bank.ReleasePendingWithdrawal(hold.EntryId, hold.Token);
        HoldsChanged?.Invoke();
        return true;
    }

    private static byte[] GetStoredData(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }

    /// <summary>Searches the target slot first, then every box and party slot, for the exact stored bytes.</summary>
    private static bool Contains(SaveFile sav, SlotLocation target, byte[] expected)
    {
        if (Matches(TryRead(sav, target), expected))
            return true;

        if (sav.HasBox)
        {
            for (var box = 0; box < sav.BoxCount; box++)
            {
                for (var slot = 0; slot < sav.BoxSlotCount; slot++)
                {
                    if (Matches(TryRead(sav, SlotLocation.FromBox(box, slot)), expected))
                        return true;
                }
            }
        }

        if (sav.HasParty)
        {
            for (var slot = 0; slot < sav.PartyCount; slot++)
            {
                if (Matches(TryRead(sav, SlotLocation.FromParty(slot)), expected))
                    return true;
            }
        }

        return false;
    }

    private static PKM? TryRead(SaveFile sav, SlotLocation location)
    {
        try
        {
            if (location.IsParty)
                return sav.HasParty && location.Slot >= 0 && location.Slot < 6 ? sav.GetPartySlotAtIndex(location.Slot) : null;
            return sav.HasBox && location.Box >= 0 && location.Box < sav.BoxCount && location.Slot >= 0 && location.Slot < sav.BoxSlotCount
                ? sav.GetBoxSlotAtIndex(location.Box, location.Slot)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Matches(PKM? pk, byte[] expected)
    {
        if (pk is null || pk.Species == 0 || pk.SIZE_STORED != expected.Length)
            return false;
        return GetStoredData(pk).AsSpan().SequenceEqual(expected);
    }
}
