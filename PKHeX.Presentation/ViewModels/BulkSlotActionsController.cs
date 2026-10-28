using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Orchestrates the bulk Move/Copy/Duplicate/Release/Export commands exposed by the box/party
/// multi-select "Actions" menu (the Organizer). For every save-mutating action this performs, in
/// order: (1) plan the operation (pure, see the Application-layer use cases), (2) exactly one
/// up-front confirmation dialog stating the count, (3) a restore point via the existing backup
/// service, (4) the mutation wrapped in one <see cref="UndoRedoService.ApplyBatch"/> transaction so the
/// whole bulk action undoes/redoes as a unit and a mid-batch failure rolls every slot back, then (5) a
/// result dialog aggregating what happened (skip-and-report, never a per-item prompt).
/// </summary>
/// <remarks>
/// One instance is created per open save (same lifecycle as <c>BoxViewer</c>/<c>PartyViewer</c>).
/// <c>MainWindowViewModel</c> calls <see cref="Retire"/> on every save change, so a dialog that was
/// still open when the save switched cannot go on to write into the old save.
/// </remarks>
public sealed class BulkSlotActionsController(
    SaveFile sav,
    UndoRedoService undoRedo,
    BoxSelectionService selection,
    IDialogService dialogService,
    ISaveBackupService? backupService,
    AppSettings? settings,
    Func<string?> getCurrentPath,
    BankHoldTracker? bankHolds = null,
    Func<string>? getBankSourceLabel = null,
    IAutoLegalityService? autoLegality = null,
    HardcoreMode? hardcore = null)
{
    private bool _retired;
    private bool _legalizing;

    private static string T(string key) => LocalizedStrings.Instance[key];
    private static string F(string key, params object?[] args) => LocalizedStrings.Instance.Format(key, args);

    /// <summary>Raised after a bulk action actually mutated the save, so callers can refresh their views.</summary>
    public event Action? Completed;

    /// <summary>The save this controller acts on.</summary>
    public SaveFile Save => sav;

    /// <summary>Whether "Legalize marked" is available (the Auto Legality Mod service was supplied).</summary>
    public bool HasLegalize => autoLegality is not null;

    /// <summary>Whether "Send marked to Bank" is available (a Bank service was supplied).</summary>
    public bool HasBank => bankHolds is not null;

    /// <summary>Whether copying, duplicating and legalizing are allowed (Hardcore mode closes them).</summary>
    public bool AllowsEditing => hardcore?.Allows(SaveAction.Duplicate) ?? true;

    /// <summary>Stops this controller from acting on its save (called when the save is closed/replaced).</summary>
    public void Retire() => _retired = true;

    private bool CanAct => !_retired && selection.Count > 0;

    private void CreateRestorePoint()
    {
        if (backupService is null || settings is null)
            return;

        var path = getCurrentPath();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var identity = SaveIdentity.Compute(path);
            var bytes = sav.Write().ToArray();
            backupService.CreateBackup(identity, bytes, settings.SaveBackup.MaxBackupsPerSave);
        }
        catch (Exception ex)
        {
            // Backing up must never block the bulk action (matches the existing backup call sites);
            // the action itself is still a single undoable transaction.
            System.Diagnostics.Trace.TraceWarning($"Organizer restore point failed: {ex.Message}");
        }
    }

    private static ISlotInfo CreateSlotInfo(SaveFile save, SlotLocation location) => location.IsParty
        ? new SlotInfoParty(location.Slot)
        : new SlotInfoBox(location.Box, location.Slot, save);

    /// <summary>
    /// Bulk move (<paramref name="clone"/> = false) or copy (= true) every marked slot into the empty
    /// slots of <paramref name="destinationArea"/>; <paramref name="destinationName"/> names that area
    /// in the confirmation dialog.
    /// </summary>
    public Task TransferMarkedAsync(SlotLocation destinationArea, bool clone, string destinationName)
    {
        var useCase = new BulkSlotTransferUseCase();
        return RunTransferAsync(
            () => useCase.Plan(sav, selection.Snapshot(), destinationArea, clone),
            clone,
            clone ? BulkKind.Copy : BulkKind.Move,
            destinationName);
    }

    /// <summary>Bulk-duplicates every marked slot into the next empty slot(s) of its own box/party.</summary>
    public Task DuplicateMarkedAsync()
    {
        var useCase = new BulkSlotTransferUseCase();
        return RunTransferAsync(() => useCase.PlanDuplicateInPlace(sav, selection.Snapshot()), clone: true, BulkKind.Duplicate, string.Empty);
    }

    private enum BulkKind { Move, Copy, Duplicate }

    private async Task RunTransferAsync(Func<BulkTransferResult> plan, bool clone, BulkKind kind, string destinationName)
    {
        if (!CanAct || (clone && !AllowsEditing))
            return;

        var planned = plan();
        if (planned.WouldEmptyParty)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), T("Organizer_WouldEmptyPartyMessage"));
            return;
        }

        if (planned.Entries.Count == 0)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), BuildSkipSummary(T("Organizer_NothingChangedMessage"), planned));
            return;
        }

        var (titleKey, messageKey, resultKey) = kind switch
        {
            BulkKind.Move => ("Organizer_ConfirmMoveTitle", "Organizer_ConfirmMoveMessage", "Organizer_ResultMoved"),
            BulkKind.Copy => ("Organizer_ConfirmCopyTitle", "Organizer_ConfirmCopyMessage", "Organizer_ResultCopied"),
            _ => ("Organizer_ConfirmDuplicateTitle", "Organizer_ConfirmDuplicateMessage", "Organizer_ResultDuplicated"),
        };

        var confirmed = await dialogService.ShowConfirmationAsync(
            T(titleKey),
            F(messageKey, planned.Entries.Count, destinationName),
            T("Common_OK"),
            T("Common_Cancel"));
        if (!confirmed || _retired)
            return;

        // Re-plan after the await: if anything changed while the dialog was open, do not act on a
        // plan the user did not confirm.
        var current = plan();
        if (!current.Entries.SequenceEqual(planned.Entries))
            return;

        CreateRestorePoint();

        var useCase = new BulkSlotTransferUseCase();
        var affected = BulkSlotTransferUseCase.GetAffectedSlots(current.Entries)
            .Select(location => CreateSlotInfo(sav, location))
            .ToArray();

        if (!await TryApplyAsync(affected, () => useCase.Execute(sav, current.Entries, clone)))
            return;

        // Processed sources are done; whatever stays marked is exactly what did not fit, so the user
        // can see it (and act again elsewhere). A move out of the party compacts it, which invalidates
        // every remaining index-based party mark.
        selection.Unmark(current.Entries.Select(e => e.Source));
        if (!clone && current.Entries.Any(e => e.Source.IsParty))
            selection.UnmarkParty();
        selection.Prune(sav);
        Completed?.Invoke();

        await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), BuildSkipSummary(F(resultKey, current.Entries.Count), current));
    }

    private static string BuildSkipSummary(string lead, BulkTransferResult plan)
    {
        var message = lead;
        if (plan.SkippedNoRoom > 0)
            message += " " + F("Organizer_SkippedNoRoom", plan.SkippedNoRoom);
        if (plan.SkippedUnavailable > 0)
            message += " " + F("Organizer_SkippedUnavailable", plan.SkippedUnavailable);
        return message;
    }

    private async Task<bool> TryApplyAsync(IReadOnlyList<ISlotInfo> affected, Action write)
    {
        try
        {
            var token = undoRedo.ApplyBatch(affected, _ => write());
            if (token is null)
            {
                await dialogService.ShowErrorAsync(T("Organizer_ActionsTitle"), T("Organizer_FailedMessage"));
                return false;
            }
        }
        catch (Exception ex)
        {
            // ApplyBatch rolls every captured slot back before the exception reaches here.
            TraceFailure(ex);
            await dialogService.ShowErrorAsync(T("Organizer_ActionsTitle"), T("Organizer_FailedMessage") + Environment.NewLine + ex.Message);
            return false;
        }

        sav.State.Edited = true;
        return true;
    }

    /// <summary>
    /// Releases (blanks) every marked box slot. Party marks are always excluded (reported, never
    /// released); any marked locked/protected box slot refuses the whole release.
    /// </summary>
    public async Task ReleaseMarkedAsync()
    {
        if (!CanAct)
            return;

        var useCase = new BulkReleaseSlotsUseCase();
        var plan = useCase.Plan(sav, selection.Snapshot());
        if (plan.IsRefused)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), F("Organizer_ReleaseRefusedMessage", plan.Protected));
            return;
        }

        if (plan.Released.Count == 0)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), T("Organizer_NothingToReleaseMessage"));
            return;
        }

        var message = F("Organizer_ConfirmReleaseMessage", plan.Released.Count);
        if (plan.ExcludedParty > 0)
            message += " " + F("Organizer_ExcludedParty", plan.ExcludedParty);
        var confirmed = await dialogService.ShowConfirmationAsync(
            T("Organizer_ConfirmReleaseTitle"),
            message,
            T("Organizer_Release"),
            T("Common_Cancel"));
        if (!confirmed || _retired)
            return;

        var current = useCase.Plan(sav, selection.Snapshot());
        if (current.IsRefused || !current.Released.SequenceEqual(plan.Released))
            return;

        CreateRestorePoint();

        var affected = current.Released.Select(location => CreateSlotInfo(sav, location)).ToArray();
        if (!await TryApplyAsync(affected, () => useCase.Execute(sav, current.Released)))
            return;

        selection.Unmark(current.Released);
        selection.Prune(sav);
        Completed?.Invoke();

        var result = F("Organizer_ResultReleased", current.Released.Count);
        if (current.ExcludedParty > 0)
            result += " " + F("Organizer_ExcludedParty", current.ExcludedParty);
        await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), result);
    }

    /// <summary>
    /// Exports every marked slot to individual decrypted entity files in a user-picked folder. Read-only
    /// for the save, so the folder picker is the only confirmation and the marks are kept.
    /// </summary>
    public async Task ExportMarkedAsync()
    {
        if (!CanAct)
            return;

        var folder = await dialogService.OpenFolderAsync(T("Organizer_SelectExportFolderTitle"));
        if (string.IsNullOrEmpty(folder) || _retired)
            return;

        BulkExportResult result;
        try
        {
            result = new BulkExportSlotsUseCase().Execute(sav, selection.Snapshot(), folder);
        }
        catch (Exception ex)
        {
            await dialogService.ShowErrorAsync(T("Organizer_ActionsTitle"), ex.Message);
            return;
        }

        var message = F("Organizer_ResultExported", result.Exported, folder);
        if (result.Skipped > 0)
            message += " " + F("Organizer_SkippedExport", result.Skipped);
        await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), message);
    }

    /// <summary>
    /// Moves every marked box/party Pokémon into the Bank (the bulk form of the slot "Send to Bank"
    /// action). Locked/protected and empty marks are skipped and reported; a deposit that would leave
    /// the party empty or with only Eggs is refused. Not an undo step, like the single deposit, and held
    /// until the save is written (closing without saving takes the Pokémon back out of the Bank).
    /// </summary>
    public async Task SendMarkedToBankAsync()
    {
        if (!CanAct || bankHolds is null)
            return;

        var useCase = new BulkBankDepositUseCase();
        var plan = useCase.Plan(sav, selection.Snapshot());
        if (plan.WouldEmptyParty)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), T("Bank_DepositLastPartyMember") + " " + T("Organizer_NothingChangedMessage"));
            return;
        }

        if (plan.Deposits.Count == 0)
        {
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), BuildDepositSkipSummary(T("Organizer_NothingChangedMessage"), plan));
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            T("Organizer_ConfirmSendToBankTitle"),
            F("Organizer_ConfirmSendToBankMessage", plan.Deposits.Count),
            T("Common_OK"),
            T("Common_Cancel"));
        if (!confirmed || _retired)
            return;

        // Re-plan after the await, as the other bulk actions do.
        var current = useCase.Plan(sav, selection.Snapshot());
        if (current.WouldEmptyParty || !current.Deposits.SequenceEqual(plan.Deposits))
            return;

        CreateRestorePoint();

        var result = useCase.Execute(sav, bankHolds, current.Deposits, getBankSourceLabel?.Invoke() ?? string.Empty, getCurrentPath());
        if (!result.Success)
        {
            await dialogService.ShowErrorAsync(
                T("Bank_DepositFailedTitle"),
                BankMessages.Describe(result.Kind, result.Error) + Environment.NewLine + T("Organizer_NothingChangedMessage"));
            return;
        }

        sav.State.Edited = true;
        selection.Unmark(current.Deposits);
        if (current.Deposits.Any(d => d.IsParty))
            selection.UnmarkParty();
        selection.Prune(sav);
        Completed?.Invoke();

        await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), BuildDepositSkipSummary(F("Organizer_ResultSentToBank", result.Added.Count), current));
    }

    /// <summary>
    /// Rebuilds every illegal marked Pokémon in place with Auto Legality Mod (the bulk form of the slot
    /// "Legalize" action). Legal ones are left alone; locked/protected and empty marks are skipped. The
    /// engine runs off the UI thread, then every success whose slot is unchanged is written in one undo
    /// transaction after a restore point. Failures stay marked and are named in the result.
    /// </summary>
    public async Task LegalizeMarkedAsync()
    {
        if (!CanAct || autoLegality is null || _legalizing || !AllowsEditing)
            return;

        var useCase = new BulkLegalizeUseCase();
        var plan = useCase.Plan(sav, selection.Snapshot());
        if (plan.Candidates.Count == 0)
        {
            var lead = plan.AlreadyLegal.Count > 0 ? T("Organizer_NothingToLegalizeMessage") : T("Organizer_NothingChangedMessage");
            await dialogService.ShowInformationAsync(T("Organizer_ActionsTitle"), BuildLegalizeSummary(lead, plan, []));
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            T("Organizer_ConfirmLegalizeTitle"),
            F("Organizer_ConfirmLegalizeMessage", plan.Candidates.Count),
            T("Common_OK"),
            T("Common_Cancel"));
        if (!confirmed || _retired)
            return;

        _legalizing = true;
        IReadOnlyList<BulkLegalizeOutcome> outcomes;
        try
        {
            var service = autoLegality;
            var candidates = plan.Candidates;
            outcomes = await Task.Run(() => useCase.Legalize(service, sav, candidates));
        }
        finally
        {
            _legalizing = false;
        }
        if (_retired)
            return;

        // Only slots that still hold exactly the planned Pokémon are written; anything edited while the
        // engine ran is left alone and reported with the failures.
        var writable = useCase.Writable(sav, outcomes);
        if (writable.Count > 0)
        {
            CreateRestorePoint();
            var affected = writable.Select(o => CreateSlotInfo(sav, o.Candidate.Location)).ToArray();
            if (!await TryApplyAsync(affected, () => useCase.Execute(sav, writable)))
                return;
        }

        var written = writable.Select(o => o.Candidate.Location).ToHashSet();
        selection.Unmark(written.Concat(plan.AlreadyLegal));
        selection.Prune(sav);
        if (writable.Count > 0)
            Completed?.Invoke();

        var failedNames = outcomes.Where(o => !written.Contains(o.Candidate.Location))
            .Select(o => DisplayName(o.Candidate.Entity))
            .ToList();
        await dialogService.ShowInformationAsync(
            T("Organizer_ActionsTitle"),
            BuildLegalizeSummary(F("Organizer_ResultLegalized", writable.Count), plan, failedNames));
    }

    private static string DisplayName(PKM pk) =>
        pk.IsNicknamed && !string.IsNullOrWhiteSpace(pk.Nickname) ? pk.Nickname : StringResourceLookup.Species(pk.Species);

    private static string BuildLegalizeSummary(string lead, BulkLegalizePlan plan, IReadOnlyList<string> failedNames)
    {
        var message = lead;
        if (plan.AlreadyLegal.Count > 0)
            message += " " + F("Organizer_SkippedAlreadyLegal", plan.AlreadyLegal.Count);
        if (plan.Unavailable > 0)
            message += " " + F("Organizer_SkippedUnavailable", plan.Unavailable);
        if (failedNames.Count > 0)
        {
            var shown = string.Join(", ", failedNames.Take(10)) + (failedNames.Count > 10 ? ", …" : string.Empty);
            message += Environment.NewLine + Environment.NewLine + F("Organizer_LegalizeFailedList", failedNames.Count, shown);
        }
        return message;
    }

    private static string BuildDepositSkipSummary(string lead, BulkBankDepositPlan plan)
    {
        var skipped = plan.SkippedEmpty + plan.SkippedProtected;
        return skipped > 0 ? lead + " " + F("Organizer_SkippedUnavailable", skipped) : lead;
    }

    // Kept at the end of the file, away from the dialog calls the localization audit scans after.
    private static void TraceFailure(Exception ex) =>
        System.Diagnostics.Trace.TraceWarning($"Organizer bulk action failed: {ex.Message}");
}
