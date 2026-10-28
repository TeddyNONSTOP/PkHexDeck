using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Living Dex autopilot: previews and then applies a rearrangement of the boxes into national dex order
/// (<see cref="LivingDexArrangeUseCase"/>). The save changes only after confirmation, as one undoable step,
/// with a restore point taken first.
/// </summary>
public partial class LivingDexArrangeViewModel : ViewModelBase
{
    private readonly SaveFile _sav;
    private readonly UndoRedoService _undoRedo;
    private readonly IDialogService _dialogs;
    private readonly ISaveBackupService? _backups;
    private readonly AppSettings? _settings;
    private readonly Func<string?> _getSavePath;
    private readonly LivingDexArrangeUseCase _useCase = new();
    private bool _retired;

    public LivingDexArrangeViewModel(SaveFile sav, UndoRedoService undoRedo, IDialogService dialogs,
        ISaveBackupService? backups = null, AppSettings? settings = null, Func<string?>? getSavePath = null)
    {
        _sav = sav;
        _undoRedo = undoRedo;
        _dialogs = dialogs;
        _backups = backups;
        _settings = settings;
        _getSavePath = getSavePath ?? (() => null);
        BoxNames = Enumerable.Range(0, sav.HasBox ? sav.BoxCount : 0).Select(GetBoxName).ToArray();
        UpdatePlan();
    }

    /// <summary>Raised after the boxes were rearranged, so open views can refresh.</summary>
    public event Action? Completed;

    public IReadOnlyList<string> BoxNames { get; }

    [ObservableProperty] private int _startBox;
    [ObservableProperty] private bool _includeForms;
    [ObservableProperty] private bool _leaveGaps = true;
    [ObservableProperty] private bool _preferShiny = true;

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _problemText = string.Empty;
    [ObservableProperty] private ObservableCollection<LivingDexArrangeMoveRow> _moves = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArrangeCommand))]
    private LivingDexArrangePlan? _plan;

    partial void OnStartBoxChanged(int value) => UpdatePlan();
    partial void OnIncludeFormsChanged(bool value) => UpdatePlan();
    partial void OnLeaveGapsChanged(bool value) => UpdatePlan();
    partial void OnPreferShinyChanged(bool value) => UpdatePlan();

    /// <summary>Stops this window from changing its save (called when the save is closed or replaced).</summary>
    public void Retire() => _retired = true;

    private LivingDexArrangeOptions Options => new(Math.Clamp(StartBox, 0, Math.Max(0, BoxNames.Count - 1)), IncludeForms, LeaveGaps, PreferShiny);

    /// <summary>Recomputes the preview from the save as it is now.</summary>
    [RelayCommand]
    public void UpdatePlan()
    {
        var plan = _useCase.Plan(_sav, Options);
        Plan = plan;

        SummaryText = LocalizedStrings.Instance.Format("LivingDexArrange_Summary", plan.Placed, plan.Missing, plan.Extras);
        var details = new List<string>();
        if (plan.Fits)
            details.Add(LocalizedStrings.Instance.Format("LivingDexArrange_Moves", plan.Moves.Count));
        if (plan.PartyOnly > 0)
            details.Add(LocalizedStrings.Instance.Format("LivingDexArrange_PartyOnly", plan.PartyOnly));
        if (plan.FixedSlots > 0)
            details.Add(LocalizedStrings.Instance.Format("LivingDexArrange_FixedSlots", plan.FixedSlots));
        DetailText = string.Join(" ", details);
        ProblemText = plan.Fits
            ? string.Empty
            : LocalizedStrings.Instance.Format("LivingDexArrange_DoesNotFit", plan.SlotsNeeded, plan.SlotsAvailable, plan.ShortBy);
        Moves = new ObservableCollection<LivingDexArrangeMoveRow>(plan.Moves.Select(m => new LivingDexArrangeMoveRow(
            GetName(m.Entity), Describe(m.FromBox, m.FromSlot), Describe(m.ToBox, m.ToSlot),
            LocalizedStrings.Instance[m.IsDexSlot ? "LivingDexArrange_KindDex" : "LivingDexArrange_KindOther"])));
    }

    private bool CanArrange => !_retired && Plan is { Fits: true, Writes.Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private async Task ArrangeAsync()
    {
        if (Plan is not { } planned || !CanArrange)
            return;

        var confirmed = await _dialogs.ShowConfirmationAsync(
            LocalizedStrings.Instance["LivingDexArrange_ConfirmTitle"],
            LocalizedStrings.Instance.Format("LivingDexArrange_ConfirmMessage", planned.Moves.Count),
            LocalizedStrings.Instance["Common_OK"],
            LocalizedStrings.Instance["Common_Cancel"]);
        if (!confirmed || _retired)
            return;

        // Re-plan after the dialog: if the boxes changed meanwhile, do not apply a plan the user did not see.
        var current = _useCase.Plan(_sav, Options);
        if (!SamePlan(planned, current))
        {
            UpdatePlan();
            await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["LivingDexArrange_Title"], LocalizedStrings.Instance["LivingDexArrange_Changed"]);
            return;
        }

        CreateRestorePoint();
        var slots = current.Writes.Select(w => (ISlotInfo)new SlotInfoBox(w.Box, w.Slot, _sav)).ToArray();
        try
        {
            if (_undoRedo.ApplyBatch(slots, _ => _useCase.Execute(_sav, current)) is null)
            {
                await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["LivingDexArrange_Title"], LocalizedStrings.Instance["LivingDexArrange_Failed"]);
                return;
            }
        }
        catch (Exception ex)
        {
            // ApplyBatch puts every captured slot back before the exception arrives here.
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["LivingDexArrange_Title"],
                LocalizedStrings.Instance["LivingDexArrange_Failed"] + Environment.NewLine + ex.Message);
            return;
        }

        _sav.State.Edited = true;
        Completed?.Invoke();
        UpdatePlan();
        await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["LivingDexArrange_Title"],
            LocalizedStrings.Instance.Format("LivingDexArrange_Done", current.Moves.Count));
    }

    private static bool SamePlan(LivingDexArrangePlan a, LivingDexArrangePlan b)
        => a.Writes.Count == b.Writes.Count
        && a.Writes.Zip(b.Writes).All(p => p.First.Box == p.Second.Box && p.First.Slot == p.Second.Slot
            && SameEntity(p.First.Entity, p.Second.Entity));

    private static bool SameEntity(PKM? a, PKM? b) => a is null || b is null
        ? a is null && b is null
        : a.Data.SequenceEqual(b.Data);

    private void CreateRestorePoint()
    {
        if (_backups is null || _settings is null || _getSavePath() is not { Length: > 0 } path)
            return;
        try
        {
            _backups.CreateBackup(SaveIdentity.Compute(path), _sav.Write().ToArray(), _settings.SaveBackup.MaxBackupsPerSave);
        }
        catch (Exception ex)
        {
            // A failed restore point never blocks the arrangement; it is still one undoable step.
            System.Diagnostics.Trace.TraceWarning($"Living Dex restore point failed: {ex.Message}");
        }
    }

    private string Describe(int box, int slot) => LocalizedStrings.Instance.Format("LivingDexTracker_InBox", GetBoxName(box), slot + 1);

    private string GetBoxName(int box) => _sav is IBoxDetailNameRead names
        ? names.GetBoxName(box)
        : BoxDetailNameExtensions.GetDefaultBoxName(box);

    private static string GetName(PKM pk)
    {
        var strings = GameInfo.Strings;
        var name = pk.Species < strings.Species.Count ? strings.Species[pk.Species] : pk.Species.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return pk.IsEgg ? $"{name} ({LocalizedStrings.Instance["LivingDexArrange_Egg"]})" : name;
    }
}

/// <summary>One row of the autopilot preview.</summary>
public sealed record LivingDexArrangeMoveRow(string Name, string From, string To, string Kind);
