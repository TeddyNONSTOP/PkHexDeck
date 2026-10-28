using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Holds a legality analysis report for display in the Legality dialog. When constructed with a save,
/// a Pokémon, and <see cref="IAutoLegalityService"/>, it also offers a "Legalize" action that runs the
/// vendored ALM engine's in-place legalization (<see cref="IAutoLegalityService.TryLegalizeExisting"/>)
/// on that Pokémon and, on success, hands the regenerated Pokémon back via <see cref="Legalized"/> for
/// the caller to load into the editor.
/// </summary>
public partial class LegalityViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SaveFile? _sav;
    private readonly PKM? _pk;
    private readonly IAutoLegalityService? _service;

    [ObservableProperty]
    private string _report;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLegalize))]
    [NotifyCanExecuteChangedFor(nameof(LegalizeCommand))]
    private bool _isLegal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLegalize))]
    [NotifyCanExecuteChangedFor(nameof(LegalizeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Raised when the "Legalize" action produces a legal Pokémon, for the caller to adopt.</summary>
    public event Action<PKM>? Legalized;

    public Action? CloseRequested { get; set; }

    /// <summary>Whether a "Legalize" action is offered at all (false for read-only usages of this dialog).</summary>
    public bool CanOfferLegalize => _sav is not null && _pk is not null && _service is not null;

    public bool CanLegalize => CanOfferLegalize && !IsLegal && !IsBusy;

    /// <summary>Read-only report, with no "Legalize" action (e.g. a pre-formatted report with no backing save/PKM).</summary>
    public LegalityViewModel(string report) : this(report, isLegal: true, sav: null, pk: null, service: null)
    {
    }

    public LegalityViewModel(string report, bool isLegal, SaveFile? sav, PKM? pk, IAutoLegalityService? service)
    {
        _report = report;
        _isLegal = isLegal;
        _sav = sav;
        _pk = pk;
        _service = service;
    }

    [RelayCommand(CanExecute = nameof(CanLegalize))]
    private async Task LegalizeAsync()
    {
        if (_sav is null || _pk is null || _service is null)
            return;

        var sav = _sav;
        var pk = _pk;
        var service = _service;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // The engine is CPU-bound (seconds); keep the UI responsive.
            var result = await Task.Run(() => service.TryLegalizeExisting(sav, pk));

            if (result.Success && result.Pokemon is not null)
            {
                Legalized?.Invoke(result.Pokemon);
                CloseRequested?.Invoke();
            }
            else
            {
                if (!string.IsNullOrEmpty(result.LegalityReport))
                    Report = result.LegalityReport;
                StatusMessage = result.Messages.Count > 0
                    ? result.MessageText
                    : LocalizedStrings.Instance["AutoLegality_Failed"];
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizedStrings.Instance.Format("LegalityView_LegalizeUnexpectedError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
