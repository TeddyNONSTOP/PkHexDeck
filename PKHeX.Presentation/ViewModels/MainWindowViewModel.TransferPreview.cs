using PKHeX.Application.UseCases;
using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Transfer preview for the main window: before a Pokémon from another format (a Bank entry, a dropped
/// file, a slot dragged from another window) is written into the open save, show what the conversion
/// changes and whether the result is legal (<see cref="TransferPreviewViewModel"/>). Turned off with
/// <see cref="AppSettings.EditorBehaviorSettings.PreviewTransfers"/>.
/// </summary>
public partial class MainWindowViewModel
{
    private bool PreviewTransfers => _settings.EditorBehavior.PreviewTransfers;

    /// <summary>Builds a preview, or <see langword="null"/> if PKHeX.Core cannot describe this entity.</summary>
    private static EntityTransferPreview? TryBuildTransferPreview(SaveFile sav, PKM source, PKM converted)
    {
        try
        {
            return new EntityTransferPreviewUseCase().Build(sav, source, converted);
        }
        catch (Exception ex)
        {
            // A preview that cannot be built never blocks the transfer; it behaves as before the preview existed.
            System.Diagnostics.Trace.TraceWarning($"Transfer preview failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Shows the preview when previews are on and (<paramref name="always"/> or one of them is worth
    /// showing). Returns <see langword="false"/> only when the user cancelled it.
    /// </summary>
    private async Task<bool> ConfirmTransferAsync(SaveFile sav, IReadOnlyList<EntityTransferPreview> previews, string confirmText, bool always)
    {
        if (!PreviewTransfers || previews.Count == 0 || (!always && !previews.Any(p => p.IsNotable)))
            return true;

        var vm = new TransferPreviewViewModel(previews, GetSaveLabel(sav), confirmText, _spriteRenderer);
        await _windowService.ShowDialogAsync(vm, T("TransferPreview_Title"));
        return vm.Confirmed;
    }
}
