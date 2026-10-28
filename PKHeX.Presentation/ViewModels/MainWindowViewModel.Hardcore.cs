using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Hardcore mode in the shell: which tools stay open, the header badge, and the explanation shown
/// when an action or a save is refused. The rule itself lives in <see cref="HardcoreMode"/>.
/// </summary>
public partial class MainWindowViewModel
{
    private readonly HardcoreMode _hardcore;

    /// <summary>
    /// Tools that only browse, report, export, back up or move, by shortcut action ID. Every other
    /// tool is closed while Hardcore mode is on, including tools added later.
    /// </summary>
    private static readonly HashSet<string> HardcoreTools = new(StringComparer.Ordinal)
    {
        "OpenFolderList",
        "OpenBoxReport",
        "OpenLivingDexTracker",
        "OpenNuzlockeReport",
        "OpenLegalityAudit",
        "OpenPKMDatabase",
        "OpenEncounterDatabase",
        "OpenMysteryGiftDatabase",
        "OpenBoxWorkspace",
        "OpenPartyWorkspace",
        "ExportShowdown",
        "ExportShowdownBox",
        "ExportShowdownAllBoxes",
        "ShowQrCode",
        "DumpBoxes",
        "OpenSaveHandlerTroubleshooter",
        "OpenBackupManager",
        "OpenSaveDiff",
        "OpenLivingDexArrange",
        "OpenPssExport",
        "OpenRsBoxWallpaper",
        "OpenGroupViewer",
    };

    public HardcoreMode Hardcore => _hardcore;

    /// <summary>True while Hardcore mode is on (header badge, read-only save tabs).</summary>
    public bool IsHardcore => _hardcore.IsEnabled;

    private void InitializeHardcore()
    {
        foreach (var item in _capabilityRegistry)
        {
            var actionId = item.ActionId;
            item.Permission = () => !_hardcore.IsEnabled || HardcoreTools.Contains(actionId);
        }
        _hardcore.Changed += OnHardcoreChanged;
    }

    private void OnHardcoreChanged() => PostToUi(() =>
    {
        // Open tool windows may edit; close them all rather than leave one writing behind the mode.
        if (_hardcore.IsEnabled)
            _windowService.CloseAllTools();
        OnPropertyChanged(nameof(IsHardcore));
        RefreshCapabilityAvailability();
        BoxViewer?.RefreshHardcore();
        PartyViewer?.RefreshHardcore();
    });

    /// <summary>True when Hardcore mode allows <paramref name="action"/>; otherwise explains why not.</summary>
    private bool AllowsOrExplain(SaveAction action)
    {
        if (_hardcore.Allows(action))
            return true;
        _ = _dialogService.ShowErrorAsync(T("Hardcore_Title"), T("Hardcore_Blocked"));
        return false;
    }

    /// <summary>
    /// Checks the save the way the write will be checked and explains a refusal. The gateway refuses
    /// the write regardless; this only turns its bare failure into a reason.
    /// </summary>
    private async Task<bool> ConfirmHardcoreWriteAsync(SaveFile save)
    {
        if (_hardcore.Verify(save) is not { } refusal)
            return true;
        await _dialogService.ShowErrorAsync(T("Hardcore_Title"), DescribeRefusal(refusal));
        return false;
    }

    private static string DescribeRefusal(SaveWriteRefusal refusal) => refusal.Kind switch
    {
        SaveWriteRefusalKind.PokemonChanged when refusal.Box >= 0 => LocalizedStrings.Instance.Format(
            "Hardcore_RefusedPokemonBox", StringResourceLookup.Species(refusal.Species), refusal.Box + 1, refusal.Slot + 1),
        SaveWriteRefusalKind.PokemonChanged => LocalizedStrings.Instance.Format(
            "Hardcore_RefusedPokemonParty", StringResourceLookup.Species(refusal.Species), refusal.Slot + 1),
        SaveWriteRefusalKind.SaveDataChanged => T("Hardcore_RefusedSaveData"),
        _ => T("Hardcore_RefusedUnverified"),
    };
}
