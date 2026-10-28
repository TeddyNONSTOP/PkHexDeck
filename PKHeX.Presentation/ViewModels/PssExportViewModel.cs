using System.Text;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Read-only Gen 6 PSS preview; export actions require an explicit click.</summary>
public partial class PssExportViewModel : ViewModelBase, ICloseableDialog
{
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;

    public Action? CloseRequested { get; set; }
    public string PreviewText { get; }

    public PssExportViewModel(SAV6 save, IClipboardService clipboard, IDialogService dialogs)
    {
        _clipboard = clipboard;
        _dialogs = dialogs;
        PreviewText = Format(new ExportPss6UseCase().Execute(save));
    }

    private static string Format(IReadOnlyList<PssGroup> groups)
    {
        var strings = LocalizedStrings.Instance;
        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            if (builder.Length > 0) builder.AppendLine().AppendLine();
            var groupName = group.Kind switch
            {
                PssGroupKind.Friends => strings["PssExport_Friends"],
                PssGroupKind.Acquaintances => strings["PssExport_Acquaintances"],
                _ => strings["PssExport_Passerby"],
            };
            builder.AppendLine(groupName).AppendLine(new string('─', groupName.Length));
            if (group.Contacts.Count == 0)
            {
                builder.AppendLine(strings["PssExport_Empty"]);
                continue;
            }
            for (var i = 0; i < group.Contacts.Count; i++)
            {
                if (i > 0) builder.AppendLine();
                var contact = group.Contacts[i];
                Append(builder, strings["PssExport_Trainer"], contact.Trainer);
                Append(builder, strings["PssExport_Message"], contact.Message);
                Append(builder, strings["PssExport_Game"], contact.Game);
                Append(builder, strings["PssExport_Country"], contact.Country);
                Append(builder, strings["PssExport_Region"], contact.Region);
                Append(builder, strings["PssExport_Favorite"], contact.FavoriteSpecies);
            }
        }
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, string value) =>
        builder.Append(label).Append(": ").AppendLine(value);

    [RelayCommand]
    private async Task CopyAsync()
    {
        try
        {
            await _clipboard.SetTextAsync(PreviewText);
            await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["PssExport_Title"],
                LocalizedStrings.Instance["PssExport_Copied"]);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveTextAsync()
    {
        var path = await _dialogs.SaveFileAsync(LocalizedStrings.Instance["PssExport_SaveText"], "pss-contacts.txt", ["*.txt"]);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await File.WriteAllTextAsync(path, PreviewText, new UTF8Encoding(false));
            await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["PssExport_Title"],
                LocalizedStrings.Instance["PssExport_Saved"]);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"], ex.Message);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
