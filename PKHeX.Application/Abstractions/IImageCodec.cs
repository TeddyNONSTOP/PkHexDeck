namespace PKHeX.Application.Abstractions;

public sealed record PixelImage(int Width, int Height, byte[] Bgra);
public enum ImageDecodeError { None, InvalidPng, WrongDimensions }
public sealed record ImageDecodeResult(PixelImage? Image, ImageDecodeError Error);

/// <summary>PNG decoding and encoding stay in the host; consumers exchange plain BGRA pixels.</summary>
public interface IImageCodec
{
    ImageDecodeResult DecodePng(byte[] png, int width, int height);
    byte[] EncodePng(PixelImage image);
}
