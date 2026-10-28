using System.IO;
using System.Runtime.InteropServices;
using PKHeX.Application.Abstractions;
using SkiaSharp;

namespace PKHeX.Avalonia.Services;

public sealed class PngImageCodec : IImageCodec
{
    public ImageDecodeResult DecodePng(byte[] png, int width, int height)
    {
        using var stream = new MemoryStream(png, false);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png) return new(null, ImageDecodeError.InvalidPng);
        if (codec.Info.Width != width || codec.Info.Height != height) return new(null, ImageDecodeError.WrongDimensions);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return new(null, ImageDecodeError.InvalidPng);
        var bytes = new byte[checked(width * height * 4)];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return new(new PixelImage(width, height, bytes), ImageDecodeError.None);
    }
    public byte[] EncodePng(PixelImage image)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(image.Bgra.Length, checked(image.Width * image.Height * 4));
        using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Marshal.Copy(image.Bgra, 0, bitmap.GetPixels(), image.Bgra.Length);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
