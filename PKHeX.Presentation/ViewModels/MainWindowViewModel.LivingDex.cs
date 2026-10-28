using CommunityToolkit.Mvvm.Input;

namespace PKHeX.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    // Cached so re-invoking the menu item focuses the existing tool window instead of opening a
    // duplicate (ShowTool keys off the ViewModel instance). Reset to null on save change.
    private LivingDexGeneratorViewModel? _livingDexGenerator;

    [RelayCommand(CanExecute = nameof(HasSave))]
    private void OpenLivingDexGenerator()
    {
        if (CurrentSave is null) return;

        if (_livingDexGenerator is null)
        {
            _livingDexGenerator = new LivingDexGeneratorViewModel(CurrentSave, _livingDexService, _undoRedo);
            _livingDexGenerator.BoxesUpdated += OnLivingDexBoxesUpdated;
        }

        _windowService.ShowTool(_livingDexGenerator, "Living Dex Generator");
    }

    private void OnLivingDexBoxesUpdated()
    {
        BoxViewer?.RefreshCurrentBox();
        PartyViewer?.RefreshParty();
    }

    // Cached like the Box Report: re-invoking focuses the open window and refreshes it. Reset on save change.
    private LivingDexTrackerViewModel? _livingDexTracker;

    [RelayCommand(CanExecute = nameof(HasSave))]
    private void OpenLivingDexTracker()
    {
        if (CurrentSave is null) return;

        if (_livingDexTracker is null)
        {
            _livingDexTracker = new LivingDexTrackerViewModel(CurrentSave, _spriteRenderer, _bankService);
            _livingDexTracker.HoldingActivated += ShowLivingDexHolding;
            _livingDexTracker.ArrangeRequested += () => OpenLivingDexArrange();
        }
        else
        {
            _livingDexTracker.Refresh();
        }

        _windowService.ShowTool(_livingDexTracker, T("Menu_Data_LivingDexTracker"));
    }

    // Cached like the tracker: re-invoking focuses the open window and refreshes it. Reset on save change.
    private NuzlockeReportViewModel? _nuzlockeReport;

    [RelayCommand(CanExecute = nameof(HasSave))]
    private void OpenNuzlockeReport()
    {
        if (CurrentSave is null) return;

        if (_nuzlockeReport is null)
        {
            _nuzlockeReport = new NuzlockeReportViewModel(CurrentSave, _spriteRenderer);
            _nuzlockeReport.HoldingActivated += ShowLivingDexHolding;
        }
        else
        {
            _nuzlockeReport.Refresh();
        }

        _windowService.ShowTool(_nuzlockeReport, T("Menu_Data_NuzlockeReport"));
    }

    // Cached like the tracker: re-invoking focuses the open window and refreshes its preview.
    private LivingDexArrangeViewModel? _livingDexArrange;

    [RelayCommand(CanExecute = nameof(HasSave))]
    private void OpenLivingDexArrange()
    {
        if (CurrentSave is null) return;

        if (_livingDexArrange is null)
        {
            _livingDexArrange = new LivingDexArrangeViewModel(CurrentSave, _undoRedo, _dialogService,
                _saveBackupService, _settings, () => _saveFileService.CurrentPath);
            _livingDexArrange.Completed += OnLivingDexArranged;
        }
        else
        {
            _livingDexArrange.UpdatePlan();
        }

        _windowService.ShowTool(_livingDexArrange, T("LivingDexArrange_Title"));
    }

    private void OnLivingDexArranged()
    {
        BoxViewer?.RefreshCurrentBox();
        _livingDexTracker?.Refresh();
    }

    private void ShowLivingDexHolding(PKHeX.Application.UseCases.LivingDexHolding holding)
    {
        var save = CurrentSave;
        if (save is null) return;

        if (holding.Source == PKHeX.Application.UseCases.LivingDexSourceKind.Party)
        {
            if (holding.Slot >= save.PartyCount) return;
            PartyViewer?.RefreshParty();
            LoadEntity(save.GetPartySlotAtIndex(holding.Slot));
            return;
        }

        if (holding.Box >= save.BoxCount || holding.Slot >= save.BoxSlotCount) return;
        ((IBoxNavigator?)BoxViewer)?.NavigateTo(holding.Box, holding.Slot);
        LoadEntity(save.GetBoxSlotAtIndex(holding.Box, holding.Slot));
    }
}
