using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.UseCases;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class MysteryGiftEditorViewModel
{
    private void ReplaceReceivedIds(IEnumerable<int> ids) => ReceivedFlags = new ObservableCollection<string>(
        ids.Select(id => id.ToString("D4", CultureInfo.InvariantCulture)));

    [RelayCommand]
    private async Task ImportReceivedIdsAsync()
    {
        if (_flags is null) return;
        var loc = LocalizedStrings.Instance;
        var path = await _dialogService.OpenFileAsync(loc["GiftPreview_ImportIds"], ["*.txt"]);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var text = await File.ReadAllTextAsync(path);
            if (!ReceivedGiftIdText.TryParse(text, _flags.MysteryGiftReceivedFlagMax, out var ids))
            {
                await _dialogService.ShowErrorAsync(loc["GiftPreview_ImportIds"], loc["GiftPreview_InvalidIds"]);
                return;
            }
            ReplaceReceivedIds(ids);
        }
        catch (IOException ex) { await _dialogService.ShowErrorAsync(loc["GiftPreview_ImportIds"], ex.Message); }
        catch (UnauthorizedAccessException ex) { await _dialogService.ShowErrorAsync(loc["GiftPreview_ImportIds"], ex.Message); }
    }

    [RelayCommand]
    private async Task ExportReceivedIdsAsync()
    {
        if (_flags is null) return;
        var loc = LocalizedStrings.Instance;
        var path = await _dialogService.SaveFileAsync(loc["GiftPreview_ExportIds"], "ReceivedGiftIds.txt", ["*.txt"]);
        if (string.IsNullOrEmpty(path)) return;
        if (!ReceivedGiftIdText.TryParse(string.Join("\n", ReceivedFlags), _flags.MysteryGiftReceivedFlagMax, out var ids))
        {
            await _dialogService.ShowErrorAsync(loc["GiftPreview_ExportIds"], loc["GiftPreview_InvalidIds"]);
            return;
        }
        try { await File.WriteAllTextAsync(path, ReceivedGiftIdText.Export(ids)); }
        catch (IOException ex) { await _dialogService.ShowErrorAsync(loc["GiftPreview_ExportIds"], ex.Message); }
        catch (UnauthorizedAccessException ex) { await _dialogService.ShowErrorAsync(loc["GiftPreview_ExportIds"], ex.Message); }
    }

    [RelayCommand]
    private Task AllUsedAsync() => SetAllReceivedIdsAsync(true);

    [RelayCommand]
    private Task AllUnusedAsync() => SetAllReceivedIdsAsync(false);

    private async Task SetAllReceivedIdsAsync(bool used)
    {
        if (_flags is null) return;
        var loc = LocalizedStrings.Instance;
        var title = loc[used ? "GiftPreview_AllUsed" : "GiftPreview_AllUnused"];
        if (await _dialogService.ShowConfirmationAsync(title, loc["GiftPreview_ConfirmIds"], title, loc["Common_Cancel"]))
            ReplaceReceivedIds(used ? Enumerable.Range(0, _flags.MysteryGiftReceivedFlagMax) : []);
    }
}
