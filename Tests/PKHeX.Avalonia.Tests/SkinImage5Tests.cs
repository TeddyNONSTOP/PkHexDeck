using System.Buffers.Binary;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class SkinImage5Tests
{
    internal static PixelImage Solid(int color)
    {
        var bytes = new byte[256 * 192 * 4];
        for (var i = 0; i < bytes.Length; i += 4) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i), color);
        return new(256, 192, bytes);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CGear_PngAndBinary_RoundTripBothFormats_AndKeepNoOpPadding(bool bw)
    {
        var image = Solid(Color15Bit.GetColorExpand(31));
        var original = new byte[CGearBackground.SIZE];
        var converted = SkinImage5.WriteCGear(image, original, bw);
        Assert.Equal(SkinImageError.None, converted.Error);
        Assert.Equal(image.Bgra, SkinImage5.ReadCGear(converted.Data!, bw).Bgra);
        converted.Data![100] = 0xA7; // Unreferenced tile bytes must survive an image no-op.
        var frame = SkinImage5.ReadCGear(converted.Data, bw);
        var codec = new PngImageCodec(); var png = codec.EncodePng(frame);
        var decoded = codec.DecodePng(png, 256, 192);
        Assert.Equal(ImageDecodeError.None, decoded.Error);
        Assert.Equal(frame.Bgra, decoded.Image!.Bgra);
        Assert.Equal(converted.Data, SkinImage5.WriteCGear(decoded.Image, converted.Data, bw).Data);
        Assert.Equal(new byte[CGearBackground.SIZE], original);
    }
    [Fact]
    public void CGear_RejectsDimensionsTransparencyExcessColorsAndExcessTiles_WithoutMutation()
    {
        var source = new byte[CGearBackground.SIZE]; var before = source.ToArray();
        Assert.Equal(SkinImageError.Dimensions, SkinImage5.WriteCGear(new(1, 1, new byte[4]), source, false).Error);
        Assert.Equal(SkinImageError.Transparency, SkinImage5.WriteCGear(Solid(0), source, false).Error);
        var image = Solid(unchecked((int)0xFF000000));
        for (var i = 0; i < 17; i++) BinaryPrimitives.WriteInt32LittleEndian(image.Bgra.AsSpan(i * 4), Color15Bit.GetColorExpand((ushort)i));
        Assert.Equal(SkinImageError.Colors, SkinImage5.WriteCGear(image, source, false).Error);
        var random = new Random(1234);
        for (var i = 0; i < image.Bgra.Length; i += 4) BinaryPrimitives.WriteInt32LittleEndian(image.Bgra.AsSpan(i), Color15Bit.GetColorExpand((ushort)(random.Next(2) * 31)));
        Assert.Equal(SkinImageError.Tiles, SkinImage5.WriteCGear(image, source, false).Error);
        Assert.Equal(before, source);
    }
    [Fact]
    public void Dex_SequentialTilesTransparencyCompositeAndBothLayers_RoundTripAndPreserveTail()
    {
        var source = new byte[PokeDexSkin5.SIZE]; var skin = new PokeDexSkin5(source);
        for (var i = 0; i < 64; i++) BinaryPrimitives.WriteUInt16LittleEndian(skin.ColorBackground[(i * 2)..], (ushort)(i * 100));
        source[^1] = 0xA7; source[0x6100] = 0xD5;
        var foreground = Solid(0);
        BinaryPrimitives.WriteInt32LittleEndian(foreground.Bgra.AsSpan(4), Color15Bit.GetColorExpand(31));
        BinaryPrimitives.WriteInt32LittleEndian(foreground.Bgra.AsSpan((8 * 4)), Color15Bit.GetColorExpand(31 << 5));
        var result = SkinImage5.WriteDex(foreground, source, false);
        Assert.Equal(SkinImageError.None, result.Error);
        Assert.Equal(foreground.Bgra, SkinImage5.ReadDex(result.Data!).Bgra);
        Assert.Equal(0xA7, result.Data![^1]); Assert.Equal(0xD5, result.Data[0x6100]);
        Assert.Equal(skin.ColorBackground.ToArray(), new PokeDexSkin5(result.Data).ColorBackground.ToArray());
        var background = SkinImage5.ReadDex(result.Data, backgroundOnly: true);
        Assert.Equal(256, background.Width); Assert.Equal(192, background.Height);
        Assert.Equal(result.Data, SkinImage5.WriteDex(background, result.Data, true).Data);
        Assert.Equal(result.Data, SkinImage5.WriteDex(foreground, result.Data, false).Data);
        var composite = SkinImage5.ReadDex(result.Data, composite: true);
        Assert.All(Enumerable.Range(0, 256 * 192), i => Assert.Equal(255, composite.Bgra[i * 4 + 3]));
        var replacement = Solid(Color15Bit.GetColorExpand(31 << 10));
        var changed = SkinImage5.WriteDex(replacement, result.Data, true);
        Assert.Equal(SkinImageError.None, changed.Error);
        Assert.Equal(replacement.Bgra, SkinImage5.ReadDex(changed.Data!, backgroundOnly: true).Bgra);
        Assert.Equal(new PokeDexSkin5(result.Data).ColorChoices.ToArray(), new PokeDexSkin5(changed.Data!).ColorChoices.ToArray());
        Assert.Equal(0xD5, changed.Data![0x6100]);
    }
    [Fact]
    public void Dex_RejectsRegionChangesPartialAlphaAndPaletteOverflow()
    {
        var source = new byte[PokeDexSkin5.SIZE]; var original = source.ToArray();
        var background = SkinImage5.ReadDex(source, backgroundOnly: true);
        BinaryPrimitives.WriteInt32LittleEndian(background.Bgra.AsSpan(0), Color15Bit.GetColorExpand(31));
        Assert.Equal(SkinImageError.BackgroundRegions, SkinImage5.WriteDex(background, source, true).Error);
        Assert.Equal(SkinImageError.Transparency, SkinImage5.WriteDex(Solid(0x7F000000), source, false).Error);
        var foreground = Solid(0);
        for (var i = 0; i < 16; i++) BinaryPrimitives.WriteInt32LittleEndian(foreground.Bgra.AsSpan(i * 4), Color15Bit.GetColorExpand((ushort)i));
        Assert.Equal(SkinImageError.Colors, SkinImage5.WriteDex(foreground, source, false).Error);
        Assert.Equal(original, source);
    }
    [Fact]
    public void PngCodec_RejectsOtherFormatsAndWrongSizes_BeforeDecodeAllocation()
    {
        var codec = new PngImageCodec();
        Assert.Equal(ImageDecodeError.InvalidPng, codec.DecodePng([1, 2, 3], 256, 192).Error);
        Assert.Equal(ImageDecodeError.WrongDimensions, codec.DecodePng(codec.EncodePng(new(1, 1, [0, 0, 0, 255])), 256, 192).Error);
    }
}
