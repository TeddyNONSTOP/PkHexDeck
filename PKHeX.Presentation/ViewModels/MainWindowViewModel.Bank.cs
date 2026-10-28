using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Bank integration for the main window: owns the app-lifetime <see cref="BankViewModel"/> (shown in a
/// persistent window that survives save changes), deposits from box/party slots, and withdrawals into
/// the open save under the <b>hold until saved</b> contract implemented by
/// <see cref="BankHoldTracker"/>. The tracker is driven from exactly three places here:
/// <see cref="ISaveFileGateway.SaveWritten"/> (the only point where an open save's edits become
/// durable — <see cref="OnSaveWrittenForBank"/>), the save-adoption path (<see cref="OnSaveSessionChangedForBank"/>,
/// called from <c>ApplySaveFileChanged</c> for open/switch/reload/close/discard), and shell close.
/// </summary>
public partial class MainWindowViewModel : IBankSaveTarget
{
    private readonly IBankService? _bankService;
    private readonly IBoxWallpaperCatalog? _wallpaperCatalog;
    private readonly BankHoldTracker? _bankHolds;
    private BankViewModel? _bank;
    private BoxViewerViewModel? _bankObservedBoxViewer;

    /// <summary>True when the host composed a Bank (always, in the real app).</summary>
    public bool HasBank => _bankService is not null;

    /// <summary>The app-lifetime Bank ViewModel, created on first use.</summary>
    public BankViewModel? Bank => _bank ??= CreateBankViewModel();

    /// <summary>Exposed for tests/diagnostics: the hold-until-saved bookkeeping.</summary>
    public BankHoldTracker? BankHolds => _bankHolds;

    public event Action? TargetChanged;

    private BankViewModel? CreateBankViewModel()
    {
        if (_bankService is null)
            return null;

        var vm = new BankViewModel(_bankService, _spriteRenderer, _dialogService, _saveBackupService, _windowService, _wallpaperCatalog, _bankHolds, _hardcore);
        vm.Target = this;
        return vm;
    }

    /// <summary>Opens (or focuses) the persistent Bank window. Available with or without an open save.</summary>
    [RelayCommand(CanExecute = nameof(HasBank))]
    private void OpenBank()
    {
        if (Bank is { } bank)
        {
            bank.RefreshCurrentBox();
            _windowService.ShowPersistent(bank, T("Bank_WindowTitle"));
        }
    }

    // --- IBankSaveTarget ------------------------------------------------

    public string? WithdrawTargetDescription
    {
        get
        {
            if (CurrentSave is not { } sav || !sav.HasBox || sav.BoxCount <= 0)
                return null;
            var box = GetWithdrawBox(sav);
            var boxName = BoxViewer is { } viewer && viewer.CurrentBox == box
                ? viewer.BoxName
                : BoxDetailNameExtensions.GetDefaultBoxName(box);
            return LocalizedStrings.Instance.Format("Bank_TargetDescription", GetSaveLabel(sav), boxName);
        }
    }

    public bool CanReceiveWithdrawal => CurrentSave is { HasBox: true, BoxCount: > 0 };

    private int GetWithdrawBox(SaveFile sav)
    {
        var box = BoxViewer?.CurrentBox ?? sav.CurrentBox;
        return Math.Clamp(box, 0, Math.Max(0, sav.BoxCount - 1));
    }

    private static string GetSaveLabel(SaveFile sav)
    {
        var game = GameInfo.GetVersionName(sav.Version);
        return string.IsNullOrWhiteSpace(sav.OT)
            ? game
            : LocalizedStrings.Instance.Format("Bank_SaveLabel", game, sav.OT);
    }

    /// <summary>
    /// Withdraws a Bank entry into the first empty, writable slot of the box the user is viewing.
    /// Converts through the same compatibility checks as file imports, reports why when it can't,
    /// writes through the undoable slot-write path, and leaves the entry in the Bank as a
    /// <i>pending</i> hold until this save is written to disk.
    /// </summary>
    public async Task WithdrawAsync(Guid entryId)
    {
        if (_bankService is null || _bankHolds is null)
            return;

        var title = T("Bank_WithdrawFailedTitle");
        if (CurrentSave is not { } sav)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_NoActiveSave"));
            return;
        }

        var sessionId = _slotService.SessionId;
        if (sessionId == Guid.Empty || !_slotService.IsCurrentSession(sessionId))
            return;

        if (!sav.HasBox || sav.BoxCount <= 0)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_SaveHasNoBoxes"));
            return;
        }

        // A hold left by an undone/cleared earlier withdrawal of this same entry no longer protects
        // anything in the save, so it may be released and the entry withdrawn again.
        _bankHolds.TryReleaseIfAbsent(entryId, sav);

        var entry = _bankService.Find(entryId);
        if (entry is null)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_Error_NotFound"));
            Bank?.RefreshCurrentBox();
            return;
        }

        if (entry.Pending is { } pending)
        {
            var message = pending.Kind == BankHoldKind.Deposit
                ? T("Bank_Error_PendingDeposit")
                : LocalizedStrings.Instance.Format("Bank_AlreadyPending", pending.SaveLabel);
            await _dialogService.ShowErrorAsync(title, message);
            return;
        }

        var raw = _bankService.GetRawData(entryId);
        if (raw is null)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_EntryMissing"));
            return;
        }

        var stored = BankEntryInfoFactory.TryReconstruct(raw, entry.Info);
        if (stored is null)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_EntryUnreadable"));
            return;
        }

        var conversion = BankEntityConversion.ConvertForSave(sav, stored);
        if (!conversion.Success || conversion.Entity is not { } converted)
        {
            await _dialogService.ShowErrorAsync(title, DescribeConversionFailure(conversion, stored, sav));
            return;
        }

        var box = GetWithdrawBox(sav);
        if (!TryFindWithdrawSlot(sav, box, converted, out var location, out var slotInfo))
        {
            await _dialogService.ShowErrorAsync(title, LocalizedStrings.Instance.Format("Bank_NoEmptyBoxSlot", box + 1));
            return;
        }

        if (TryBuildTransferPreview(sav, stored, converted) is { } preview)
        {
            if (!await ConfirmTransferAsync(sav, [preview], T("TransferPreview_Withdraw"), always: false))
                return;
            // The dialog awaited: the save, its session or the box may have changed meanwhile.
            if (!ReferenceEquals(CurrentSave, sav) || !_slotService.IsCurrentSession(sessionId))
                return;
            if (!TryFindWithdrawSlot(sav, box, converted, out location, out slotInfo))
            {
                await _dialogService.ShowErrorAsync(title, LocalizedStrings.Instance.Format("Bank_NoEmptyBoxSlot", box + 1));
                return;
            }
        }

        var reserve = _bankHolds.Reserve(entryId, sav, location, GetSaveLabel(sav), _saveFileService.CurrentPath, out var token);
        if (!reserve.Success)
        {
            await _dialogService.ShowErrorAsync(title, BankMessages.Describe(reserve));
            Bank?.RefreshCurrentBox();
            return;
        }

        try
        {
            var batch = _undoRedo.ApplyBatch([slotInfo], _ => WriteSlot(sav, location, converted.Clone()));
            if (batch is null)
            {
                _bankHolds.Abort(entryId, token);
                await _dialogService.ShowErrorAsync(title, T("Bank_WithdrawWriteRefused"));
                return;
            }
        }
        catch (Exception ex)
        {
            // ApplyBatch rolled the save back; release the hold so the entry is withdrawable again.
            _bankHolds.Abort(entryId, token);
            await _dialogService.ShowErrorAsync(title, LocalizedStrings.Instance.Format("Bank_WithdrawWriteFailed", ex.Message));
            return;
        }

        _bankHolds.Confirm(entryId, token, ReadSlot(sav, location));
        _hardcore.RecordArrival(sav, ReadSlot(sav, location));
        sav.State.Edited = true;
        BoxViewer?.RefreshCurrentBox();
        PartyViewer?.RefreshParty();
        BatchEditor?.RefreshExternalState();

        var name = string.IsNullOrWhiteSpace(entry.Info.Nickname) ? StringResourceLookup.Species(entry.Info.Species) : entry.Info.Nickname;
        _bank?.ShowStatus(LocalizedStrings.Instance.Format("Bank_WithdrawPendingStatus", name, location.Box + 1, location.Slot + 1));
    }

    private sealed record PlannedWithdrawal(Guid EntryId, BankEntry Entry, PKM Source, PKM Entity, SlotLocation Location, ISlotInfo SlotInfo);

    private sealed record WithdrawalPlan(IReadOnlyList<PlannedWithdrawal> Items, int NoRoom, int Incompatible, int Unavailable)
    {
        public bool SameTargets(WithdrawalPlan other) => Items.Select(i => (i.EntryId, i.Location)).SequenceEqual(other.Items.Select(i => (i.EntryId, i.Location)));
    }

    /// <summary>
    /// Plans a bulk withdrawal without writing anything: each entry must be present, not held, readable
    /// and convertible, and gets the next empty writable box slot from <paramref name="startBox"/> onward.
    /// </summary>
    private WithdrawalPlan PlanWithdrawals(SaveFile sav, IReadOnlyList<Guid> entryIds, int startBox)
    {
        var items = new List<PlannedWithdrawal>();
        var reserved = new HashSet<SlotLocation>();
        int noRoom = 0, incompatible = 0, unavailable = 0;

        foreach (var id in entryIds.Distinct())
        {
            _bankHolds!.TryReleaseIfAbsent(id, sav);
            var entry = _bankService!.Find(id);
            var raw = entry is { Pending: null } ? _bankService.GetRawData(id) : null;
            var stored = raw is null ? null : BankEntryInfoFactory.TryReconstruct(raw, entry!.Info);
            if (stored is null)
            {
                unavailable++;
                continue;
            }

            var conversion = BankEntityConversion.ConvertForSave(sav, stored);
            if (!conversion.Success || conversion.Entity is not { } converted)
            {
                incompatible++;
                continue;
            }

            if (!TryFindWithdrawSlot(sav, startBox, sav.BoxCount, converted, reserved, out var location, out var slotInfo))
            {
                noRoom++;
                continue;
            }

            reserved.Add(location);
            items.Add(new PlannedWithdrawal(id, entry!, stored, converted, location, slotInfo));
        }

        return new WithdrawalPlan(items, noRoom, incompatible, unavailable);
    }

    /// <summary>
    /// Bulk form of <see cref="WithdrawAsync"/> for the Bank window's marked entries (and so the relay
    /// for moving Pokémon between two app windows: send marked to Bank in one, withdraw marked in the
    /// other). One confirmation, every hold reserved before anything is written, one undoable write
    /// for all slots, and one summary of what was skipped and why.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> WithdrawManyAsync(IReadOnlyList<Guid> entryIds)
    {
        if (_bankService is null || _bankHolds is null || entryIds.Count == 0)
            return [];

        var title = T("Bank_WithdrawFailedTitle");
        if (CurrentSave is not { } sav)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_NoActiveSave"));
            return [];
        }

        var sessionId = _slotService.SessionId;
        if (sessionId == Guid.Empty || !_slotService.IsCurrentSession(sessionId))
            return [];

        if (!sav.HasBox || sav.BoxCount <= 0)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_SaveHasNoBoxes"));
            return [];
        }

        var startBox = GetWithdrawBox(sav);
        var plan = PlanWithdrawals(sav, entryIds, startBox);
        if (plan.Items.Count == 0)
        {
            await _dialogService.ShowInformationAsync(T("Bank_ConfirmWithdrawMarkedTitle"), DescribeWithdrawalSkips(T("Organizer_NothingChangedMessage"), plan));
            return [];
        }

        // With previews on, the preview of every planned Pokémon is the confirmation.
        var previews = PreviewTransfers
            ? plan.Items.Select(i => TryBuildTransferPreview(sav, i.Source, i.Entity)).OfType<EntityTransferPreview>().ToList()
            : [];
        var confirmed = previews.Count > 0
            ? await ConfirmTransferAsync(sav, previews, T("TransferPreview_Withdraw"), always: true)
            : await _dialogService.ShowConfirmationAsync(
                T("Bank_ConfirmWithdrawMarkedTitle"),
                LocalizedStrings.Instance.Format("Bank_ConfirmWithdrawMarkedMessage", plan.Items.Count, GetSaveLabel(sav)),
                T("Common_OK"),
                T("Common_Cancel"));
        if (!confirmed || !ReferenceEquals(CurrentSave, sav) || !_slotService.IsCurrentSession(sessionId))
            return [];

        // Re-plan after the await, as the Organizer does: never act on a plan the user did not confirm.
        var current = PlanWithdrawals(sav, entryIds, startBox);
        if (!current.SameTargets(plan))
            return [];

        var tokens = new List<(Guid EntryId, Guid Token)>(current.Items.Count);
        void AbortAll()
        {
            foreach (var (id, token) in tokens)
                _bankHolds.Abort(id, token);
        }

        foreach (var item in current.Items)
        {
            var reserve = _bankHolds.Reserve(item.EntryId, sav, item.Location, GetSaveLabel(sav), _saveFileService.CurrentPath, out var token);
            if (!reserve.Success)
            {
                AbortAll();
                await _dialogService.ShowErrorAsync(title, BankMessages.Describe(reserve));
                Bank?.RefreshCurrentBox();
                return [];
            }
            tokens.Add((item.EntryId, token));
        }

        try
        {
            var batch = _undoRedo.ApplyBatch(current.Items.Select(i => i.SlotInfo).ToArray(), _ =>
            {
                foreach (var item in current.Items)
                    WriteSlot(sav, item.Location, item.Entity.Clone());
            });
            if (batch is null)
            {
                AbortAll();
                await _dialogService.ShowErrorAsync(title, T("Bank_WithdrawWriteRefused"));
                return [];
            }
        }
        catch (Exception ex)
        {
            // ApplyBatch rolled every slot back; release the holds so the entries can be withdrawn again.
            AbortAll();
            await _dialogService.ShowErrorAsync(title, LocalizedStrings.Instance.Format("Bank_WithdrawWriteFailed", ex.Message));
            return [];
        }

        for (var i = 0; i < current.Items.Count; i++)
        {
            _bankHolds.Confirm(tokens[i].EntryId, tokens[i].Token, ReadSlot(sav, current.Items[i].Location));
            _hardcore.RecordArrival(sav, ReadSlot(sav, current.Items[i].Location));
        }
        sav.State.Edited = true;
        BoxViewer?.RefreshCurrentBox();
        PartyViewer?.RefreshParty();
        BatchEditor?.RefreshExternalState();
        Bank?.RefreshCurrentBox();

        var result = DescribeWithdrawalSkips(LocalizedStrings.Instance.Format("Bank_WithdrawMarkedResult", current.Items.Count), current);
        _bank?.ShowStatus(result);
        await _dialogService.ShowInformationAsync(T("Bank_ConfirmWithdrawMarkedTitle"), result);
        return current.Items.Select(i => i.EntryId).ToList();
    }

    private static string DescribeWithdrawalSkips(string lead, WithdrawalPlan plan)
    {
        var loc = LocalizedStrings.Instance;
        var message = lead;
        if (plan.NoRoom > 0)
            message += " " + loc.Format("Organizer_SkippedNoRoom", plan.NoRoom);
        if (plan.Incompatible > 0)
            message += " " + loc.Format("Bank_SkippedIncompatible", plan.Incompatible);
        if (plan.Unavailable > 0)
            message += " " + loc.Format("Bank_SkippedUnavailable", plan.Unavailable);
        return message;
    }

    private static bool TryFindWithdrawSlot(SaveFile sav, int box, PKM pk, out SlotLocation location, out ISlotInfo slotInfo) =>
        TryFindWithdrawSlot(sav, box, box + 1, pk, null, out location, out slotInfo);

    /// <summary>First empty, writable slot in boxes <paramref name="startBox"/> up to (not including) <paramref name="endBox"/>, skipping <paramref name="reserved"/>.</summary>
    private static bool TryFindWithdrawSlot(SaveFile sav, int startBox, int endBox, PKM pk, HashSet<SlotLocation>? reserved, out SlotLocation location, out ISlotInfo slotInfo)
    {
        for (var box = startBox; box < endBox; box++)
        {
            if (TryFindWithdrawSlotInBox(sav, box, pk, reserved, out location, out slotInfo))
                return true;
        }

        location = default;
        slotInfo = null!;
        return false;
    }

    private static bool TryFindWithdrawSlotInBox(SaveFile sav, int box, PKM pk, HashSet<SlotLocation>? reserved, out SlotLocation location, out ISlotInfo slotInfo)
    {
        for (var slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            var candidate = SlotLocation.FromBox(box, slot);
            if (reserved?.Contains(candidate) == true)
                continue;
            if (sav.GetBoxSlotAtIndex(box, slot).Species != 0)
                continue;
            if (sav.IsBoxSlotOverwriteProtected(box, slot))
                continue;

            var info = CreateSlotInfo(sav, candidate);
            if (info.Type == StorageSlotType.Party) // LGPE party members live in box storage
                continue;
            if (!info.CanWriteTo(sav) || info.CanWriteTo(sav, pk) != WriteBlockedMessage.None)
                continue;

            location = candidate;
            slotInfo = info;
            return true;
        }

        location = default;
        slotInfo = null!;
        return false;
    }

    private static string DescribeConversionFailure(BankConversionResult conversion, PKM stored, SaveFile sav)
    {
        var loc = LocalizedStrings.Instance;
        var summary = conversion.Failure switch
        {
            BankConversionFailure.NoConversionRoute => loc.Format("Bank_ConvertNoRoute", stored.GetType().Name, sav.PKMType.Name),
            BankConversionFailure.IncompatibleLanguage => loc["Bank_ConvertLanguage"],
            BankConversionFailure.IncompatibleWithSave => loc.Format("Bank_ConvertIncompatible", GameInfo.GetVersionName(sav.Version)),
            _ => loc["Bank_ConvertError"],
        };
        var details = conversion.Details.Where(d => !string.IsNullOrWhiteSpace(d)).Take(8).ToList();
        return details.Count == 0 ? summary : summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, details);
    }

    // --- deposit ---------------------------------------------------------

    private void OnSendToBankRequested(SlotLocation location) => _ = SendToBankAsync(location).ContinueWith(
        t => System.Diagnostics.Trace.TraceWarning($"Send to Bank failed: {t.Exception?.GetBaseException().Message}"),
        TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>
    /// Deposits a box/party Pokémon into the Bank and clears its slot. The slot clear follows the
    /// same rules as the existing slot paths: locked/overwrite-protected slots (and LGPE party
    /// members stored in boxes) are refused, a deposit may not leave the party empty or with only
    /// Eggs, the party is compacted exactly like a party-to-box move, and the save is marked edited.
    /// Like the existing Delete action this is not an undo step (an undone deposit would put a second
    /// copy back into the save while the Bank keeps its own). If the slot cannot be cleared, the
    /// just-added Bank entry is removed again so a deposit never silently duplicates. The entry is
    /// held until the save is written (see <see cref="BankHoldTracker.Deposit"/>): closing the save
    /// without saving removes it from the Bank again.
    /// </summary>
    public async Task SendToBankAsync(SlotLocation location)
    {
        if (_bankService is null || _bankHolds is null || CurrentSave is not { } sav)
            return;

        var sessionId = _slotService.SessionId;
        if (sessionId == Guid.Empty || !_slotService.IsCurrentSession(sessionId))
            return;
        if (!IsValidSlot(sav, location))
            return;

        var pk = ReadSlot(sav, location);
        if (pk.Species == 0)
            return;

        var title = T("Bank_DepositFailedTitle");
        var slotInfo = CreateSlotInfo(sav, location);
        if (!slotInfo.CanWriteTo(sav) || (!location.IsParty && sav.IsBoxSlotOverwriteProtected(location.Box, location.Slot)))
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_DepositLocked"));
            return;
        }

        if (!location.IsParty && slotInfo.Type == StorageSlotType.Party)
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_DepositPartyMember"));
            return;
        }

        if (location.IsParty && (sav.PartyCount <= 1 || sav.IsPartyAllEggs(location.Slot)))
        {
            await _dialogService.ShowErrorAsync(title, T("Bank_DepositLastPartyMember"));
            return;
        }

        var added = _bankHolds.Deposit(sav, pk, location, GetSaveLabel(sav), _saveFileService.CurrentPath);
        if (!added.Success || added.Entry is null)
        {
            await _dialogService.ShowErrorAsync(title, BankMessages.Describe(added));
            return;
        }

        try
        {
            if (location.IsParty)
                sav.DeletePartySlot(location.Slot);
            else
                sav.SetBoxSlotAtIndex(sav.BlankPKM, location.Box, location.Slot);
        }
        catch (Exception ex)
        {
            TryRestoreSlot(sav, location, pk);
            _bankHolds.CancelDeposit(added.Entry.Id);
            await _dialogService.ShowErrorAsync(title, LocalizedStrings.Instance.Format("Bank_DepositClearFailed", ex.Message));
            Bank?.RefreshCurrentBox();
            return;
        }

        sav.State.Edited = true;
        BoxViewer?.RefreshCurrentBox();
        PartyViewer?.RefreshParty();
        BatchEditor?.RefreshExternalState();

        var info = added.Entry.Info;
        var name = string.IsNullOrWhiteSpace(info.Nickname) ? StringResourceLookup.Species(info.Species) : info.Nickname;
        if (_bank is { } bank)
        {
            bank.RefreshCurrentBox();
            bank.ShowStatus(LocalizedStrings.Instance.Format("Bank_DepositedStatus", name, added.Entry.Box + 1, added.Entry.Slot + 1));
        }
    }

    private static void TryRestoreSlot(SaveFile sav, SlotLocation location, PKM original)
    {
        try
        {
            if (ReadSlot(sav, location).Species != 0)
                return;
            WriteSlot(sav, location, original);
        }
        catch
        {
            // best effort: the original is still in the Bank's rollback path only if Remove fails
        }
    }

    // --- tracker hooks ---------------------------------------------------

    /// <summary>
    /// <see cref="ISaveFileGateway.SaveWritten"/>: the open save is now durable on disk, so settle its
    /// holds against the snapshot that was actually written.
    /// </summary>
    private void OnSaveWrittenForBank(SaveFileWrittenEventArgs args) => PostToUi(() => SettleBankHolds(args));

    // A deposit whose session ends during a running write waits for that write to decide (see BankHoldTracker).
    private void OnSaveWriteStartedForBank(SaveFile save) => PostToUi(() => _bankHolds?.OnSaveWriteStarted(save));

    private void OnSaveWriteFailedForBank(SaveFile save) => PostToUi(() => ReportCancelledDeposits(_bankHolds?.OnSaveWriteFailed(save) ?? []));

    private void ReportCancelledDeposits(IReadOnlyList<BankHoldOutcome> outcomes)
    {
        var cancelled = outcomes.Count(o => o.Resolution == BankHoldResolution.DepositCancelled);
        if (cancelled == 0)
            return;
        _bank?.RefreshCurrentBox();
        _bank?.ShowStatus(LocalizedStrings.Instance.Format("Bank_DepositCancelledStatus", cancelled));
    }

    private void SettleBankHolds(SaveFileWrittenEventArgs args)
    {
        if (_bankHolds is null)
            return;

        var outcomes = _bankHolds.OnSaveWritten(args.Original, args.Written);
        if (outcomes.Count == 0)
            return;

        var removed = outcomes.Count(o => o.Resolution == BankHoldResolution.RemovedFromBank);
        var deposited = outcomes.Count(o => o.Resolution == BankHoldResolution.DepositCompleted);
        var kept = outcomes.Where(o => o.Resolution is BankHoldResolution.KeptInBank or BankHoldResolution.BankRefused).ToList();
        _bank?.RefreshCurrentBox();
        if (removed > 0)
            _bank?.ShowStatus(LocalizedStrings.Instance.Format("Bank_WithdrawCompletedStatus", removed));
        if (deposited > 0)
            _bank?.ShowStatus(LocalizedStrings.Instance.Format("Bank_DepositCompletedStatus", deposited));
        ReportCancelledDeposits(outcomes);

        if (kept.Count > 0)
        {
            var names = string.Join(Environment.NewLine, kept.Select(o => o.Info is { } i
                ? (string.IsNullOrWhiteSpace(i.Nickname) ? StringResourceLookup.Species(i.Species) : i.Nickname)
                : o.EntryId.ToString("N")[..8]));
            _ = _dialogService.ShowInformationAsync(
                T("Bank_WithdrawKeptTitle"),
                LocalizedStrings.Instance.Format("Bank_WithdrawKeptMessage", names));
        }
    }

    /// <summary>Called from <c>ApplySaveFileChanged</c>: holds of any other save session are released.</summary>
    private void OnSaveSessionChangedForBank(SaveFile? current)
    {
        ReportCancelledDeposits(_bankHolds?.OnSaveSessionChanged(current) ?? []);
        TargetChanged?.Invoke();
        _bank?.RefreshCurrentBox();
    }

    /// <summary>Wires a freshly created Box/Party viewer pair to the Bank (deposit action + target box tracking).</summary>
    private void AttachBankToViewers(BoxViewerViewModel boxViewer, PartyViewerViewModel partyViewer)
    {
        DetachBankFromViewers();
        boxViewer.CanSendToBank = HasBank;
        partyViewer.CanSendToBank = HasBank;
        boxViewer.SendToBankRequested += OnSendToBankRequested;
        partyViewer.SendToBankRequested += OnSendToBankRequested;
        boxViewer.PropertyChanged += OnBankObservedBoxViewerChanged;
        _bankObservedBoxViewer = boxViewer;
    }

    private void DetachBankFromViewers()
    {
        if (BoxViewer is { } box)
            box.SendToBankRequested -= OnSendToBankRequested;
        if (PartyViewer is { } party)
            party.SendToBankRequested -= OnSendToBankRequested;
        if (_bankObservedBoxViewer is { } observed)
            observed.PropertyChanged -= OnBankObservedBoxViewerChanged;
        _bankObservedBoxViewer = null;
    }

    private void OnBankObservedBoxViewerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoxViewerViewModel.CurrentBox) or nameof(BoxViewerViewModel.BoxName))
            TargetChanged?.Invoke();
    }

    /// <summary>
    /// The main window closed. Any withdrawal still pending can never be saved now, so release it
    /// (the Pokémon stays in the Bank). A crash skips this; the next start releases such holds.
    /// </summary>
    public void OnShellClosed() => _bankHolds?.ReleaseAll();
}
