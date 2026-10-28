using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class DLC5EditorViewModel
{
    [ObservableProperty] private byte[]? _cGearPng;
    [ObservableProperty] private byte[]? _dexForegroundPng;
    [ObservableProperty] private byte[]? _dexBackgroundPng;
    [ObservableProperty] private byte[]? _dexCompositePng;
    [ObservableProperty] private string _imageStatus = string.Empty;
    [ObservableProperty] private int _selectedTab;
    public bool CanLoadImage => _imageCodec is not null;
    public bool CanExportCGearImage => CanLoadImage && CGearPng is not null;
    public bool CanExportDexImage => CanLoadImage && DexForegroundPng is not null;
    public bool CanExportDecrypted => BattleVideoIndex is >= 0 and < 4 && !new BattleVideo5(_sav.GetBattleVideo(BattleVideoIndex)).IsUninitialized;
    private string L(string key) => LocalizedStrings.Instance[key];
    private void RefreshImages()
    {
        if (_imageCodec is null) return;
        CGearPng = null; DexForegroundPng = null; DexBackgroundPng = null; DexCompositePng = null;
        ImageStatus = string.Empty;
        try
        {
            var gear = _sav.CGearSkinData.ToArray();
            if (!(IsBW ? new CGearBackgroundBW(gear).IsUninitialized : new CGearBackgroundB2W2(gear).IsUninitialized))
                CGearPng = _imageCodec.EncodePng(SkinImage5.ReadCGear(gear, IsBW));
        }
        catch (ArgumentException) { ImageStatus = L("DlcImages_InvalidBinary"); }
        try
        {
            var dex = _sav.PokedexSkinData.ToArray();
            if (!new PokeDexSkin5(dex).IsUninitialized)
            {
                DexForegroundPng = _imageCodec.EncodePng(SkinImage5.ReadDex(dex));
                DexBackgroundPng = _imageCodec.EncodePng(SkinImage5.ReadDex(dex, backgroundOnly: true));
                DexCompositePng = _imageCodec.EncodePng(SkinImage5.ReadDex(dex, composite: true));
            }
        }
        catch (ArgumentException) { ImageStatus = L("DlcImages_InvalidBinary"); }
        OnPropertyChanged(nameof(CanExportCGearImage)); OnPropertyChanged(nameof(CanExportDexImage));
        ExportCGearImageCommand.NotifyCanExecuteChanged(); ExportDexForegroundCommand.NotifyCanExecuteChanged(); ExportDexBackgroundCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanLoadImage))] private Task ImportCGearImage() => ImportImage(0);
    [RelayCommand(CanExecute = nameof(CanLoadImage))] private Task ImportDexForeground() => ImportImage(1);
    [RelayCommand(CanExecute = nameof(CanLoadImage))] private Task ImportDexBackground() => ImportImage(2);
    private async Task ImportImage(int kind)
    {
        if (_imageCodec is null) return;
        var path = await _dialogService.OpenFileAsync(L("DlcImages_LoadPng"), ["png"]);
        if (path is null) return;
        var bytes = await TryReadAllBytesAsync(path); if (bytes is null) return;
        var decoded = _imageCodec.DecodePng(bytes, SkinImage5.Width, SkinImage5.Height);
        if (decoded.Image is null)
        {
            await ImageError(decoded.Error == ImageDecodeError.WrongDimensions ? "DlcImages_Dimensions" : "DlcImages_InvalidPng"); return;
        }
        var result = kind == 0 ? SkinImage5.WriteCGear(decoded.Image, _sav.CGearSkinData.ToArray(), IsBW)
            : SkinImage5.WriteDex(decoded.Image, _sav.PokedexSkinData.ToArray(), kind == 2);
        if (result.Data is null)
        {
            var key = result.Error switch
            {
                SkinImageError.Colors => kind == 1 ? "DlcImages_ForegroundColors" : "DlcImages_Colors",
                SkinImageError.Tiles => "DlcImages_Tiles", SkinImageError.Transparency => "DlcImages_Transparency",
                SkinImageError.BackgroundRegions => "DlcImages_BackgroundRegions", _ => "DlcImages_InvalidBinary",
            };
            await ImageError(key); return;
        }
        var original = kind == 0 ? _sav.CGearSkinData : _sav.PokedexSkinData;
        if (!original.Span.SequenceEqual(result.Data))
        {
            if (kind == 0) _sav.SetCGearSkin(result.Data); else _sav.SetPokeDexSkin(result.Data);
        }
        RefreshImages(); ImageStatus = L("DlcImages_Imported");
    }
    private async Task ImageError(string key)
    {
        ImageStatus = L(key); await _dialogService.ShowErrorAsync(L("Common_Error"), ImageStatus);
    }
    [RelayCommand(CanExecute = nameof(CanExportCGearImage))] private Task ExportCGearImage() => ExportImage(CGearPng, "CGear_Skin.png");
    [RelayCommand(CanExecute = nameof(CanExportDexImage))] private Task ExportDexForeground() => ExportImage(DexForegroundPng, "Pokedex_Foreground.png");
    [RelayCommand(CanExecute = nameof(CanExportDexImage))] private Task ExportDexBackground() => ExportImage(DexBackgroundPng, "Pokedex_Background.png");
    private async Task ExportImage(byte[]? bytes, string name)
    {
        if (bytes is null) return;
        var path = await _dialogService.SaveFileAsync(L("DlcImages_SavePng"), name, ["png"]);
        if (path is null) return;
        if (await TryWriteAllBytesAsync(path, bytes)) ImageStatus = L("DlcImages_Exported");
    }
    [RelayCommand(CanExecute = nameof(CanExportDecrypted))]
    private async Task ExportBattleVideoDecrypted()
    {
        if (!CanExportDecrypted) return;
        var slot = BattleVideoIndex;
        var path = await _dialogService.SaveFileAsync(L("DlcImages_ExportDecrypted"), $"BattleVideo_{slot:00}_decrypted.{BattleVideo5.Extension}", [BattleVideo5.Extension]);
        if (path is null) return;
        var bytes = new ExportBattleVideo5UseCase().Execute(_sav, slot);
        if (await TryWriteAllBytesAsync(path, bytes)) ImageStatus = L("DlcImages_Exported");
    }
}
