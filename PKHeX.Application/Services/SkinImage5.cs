using System.Buffers.Binary;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

public enum SkinImageError { None, Dimensions, Transparency, Colors, Tiles, Binary, BackgroundRegions }
public sealed record SkinImageResult(byte[]? Data, SkinImageError Error);

/// <summary>Pure Gen 5 skin/pixel conversion; PNG encoding belongs to the host.</summary>
public static class SkinImage5
{
    public const int Width = 256;
    public const int Height = 192;
    private const int Pixels = Width * Height;
    private static readonly Lazy<(byte[] Indices, bool[] Overrides)> Background = new(ReadBackground);

    public static PixelImage ReadCGear(byte[] data, bool bw)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(data.Length, CGearBackground.SIZE);
        return new(Width, Height, bw ? new CGearBackgroundBW(data).GetImageData() : new CGearBackgroundB2W2(data).GetImageData());
    }
    public static SkinImageResult WriteCGear(PixelImage image, byte[] original, bool bw)
    {
        if (!Valid(image)) return new(null, SkinImageError.Dimensions);
        if (original.Length != CGearBackground.SIZE) return new(null, SkinImageError.Binary);
        var normalized = Normalize(image.Bgra, false, out var error);
        if (error != SkinImageError.None) return new(null, error);
        if (ColorCount(normalized) > 16) return new(null, SkinImageError.Colors);
        if (CountTiles(normalized) > 255) return new(null, SkinImageError.Tiles);
        try { if (ReadCGear(original, bw).Bgra.AsSpan().SequenceEqual(normalized)) return new(original.ToArray(), SkinImageError.None); }
        catch (ArgumentException) { } // A replacement may repair malformed existing tile indices.
        var result = original.ToArray();
        if (bw) new CGearBackgroundBW(result).SetImageData(normalized);
        else new CGearBackgroundB2W2(result).SetImageData(normalized);
        return new(result, SkinImageError.None);
    }
    public static PixelImage ReadDex(byte[] data, bool backgroundOnly = false, bool composite = false)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(data.Length, PokeDexSkin5.SIZE);
        var skin = new PokeDexSkin5(data);
        var foreground = new int[16]; var background = new int[64];
        PaletteColorSet.Read(skin.ColorForeground, foreground); PaletteColorSet.Read(skin.ColorBackground, background);
        var bytes = new byte[Pixels * 4]; var map = Background.Value;
        for (var tile = 0; tile < 768; tile++)
            for (var p = 0; p < 64; p++)
            {
                var packed = skin.ColorChoices[tile * 32 + p / 2];
                var choice = (p & 1) == 0 ? packed & 15 : packed >> 4;
                var index = ((tile / 32 * 8 + p / 8) * Width) + tile % 32 * 8 + p % 8;
                var color = backgroundOnly || composite && (choice == 0 || map.Overrides[index])
                    ? background[map.Indices[index]] : choice == 0 ? 0 : foreground[choice];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), color);
            }
        return new(Width, Height, bytes);
    }
    public static SkinImageResult WriteDex(PixelImage image, byte[] original, bool backgroundOnly)
    {
        if (!Valid(image)) return new(null, SkinImageError.Dimensions);
        if (original.Length != PokeDexSkin5.SIZE) return new(null, SkinImageError.Binary);
        var normalized = Normalize(image.Bgra, !backgroundOnly, out var error);
        if (error != SkinImageError.None) return new(null, error);
        if (ReadDex(original, backgroundOnly).Bgra.AsSpan().SequenceEqual(normalized)) return new(original.ToArray(), SkinImageError.None);
        var result = original.ToArray(); var skin = new PokeDexSkin5(result);
        if (backgroundOnly)
        {
            var colors = new int?[64]; var indices = Background.Value.Indices;
            for (var index = 0; index < Pixels; index++)
            {
                var color = BinaryPrimitives.ReadInt32LittleEndian(normalized.AsSpan(index * 4));
                var palette = indices[index];
                if (colors[palette] is { } existing && existing != color) return new(null, SkinImageError.BackgroundRegions);
                colors[palette] = color;
            }
            for (var i = 0; i < 64; i++)
                if (colors[i] is { } color)
                {
                    var compressed = Color15Bit.GetColorCompress(color);
                    var old = BinaryPrimitives.ReadUInt16LittleEndian(skin.ColorBackground[(i * 2)..]);
                    if ((old & 0x7FFF) != compressed) BinaryPrimitives.WriteUInt16LittleEndian(skin.ColorBackground[(i * 2)..], compressed);
                }
        }
        else
        {
            var palette = new List<int> { 0 }; // Index zero is the game's transparent/background color.
            for (var index = 0; index < Pixels; index++)
            {
                var color = BinaryPrimitives.ReadInt32LittleEndian(normalized.AsSpan(index * 4));
                if (!palette.Contains(color)) palette.Add(color);
                if (palette.Count > 16) return new(null, SkinImageError.Colors);
            }
            // Retain the reserved first color and all unused palette words.
            for (var i = 1; i < palette.Count; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(skin.ColorForeground[(i * 2)..], Color15Bit.GetColorCompress(palette[i]));
            for (var tile = 0; tile < 768; tile++)
                for (var p = 0; p < 64; p += 2)
                {
                    var index = ((tile / 32 * 8 + p / 8) * Width) + tile % 32 * 8 + p % 8;
                    var a = palette.IndexOf(BinaryPrimitives.ReadInt32LittleEndian(normalized.AsSpan(index * 4)));
                    var b = palette.IndexOf(BinaryPrimitives.ReadInt32LittleEndian(normalized.AsSpan((index + 1) * 4)));
                    skin.ColorChoices[tile * 32 + p / 2] = (byte)(a | b << 4);
                }
        }
        return new(result, SkinImageError.None);
    }
    private static bool Valid(PixelImage image) => image.Width == Width && image.Height == Height && image.Bgra.Length == Pixels * 4;
    private static byte[] Normalize(byte[] source, bool transparent, out SkinImageError error)
    {
        error = SkinImageError.None; var result = new byte[source.Length];
        for (var index = 0; index < source.Length; index += 4)
        {
            var alpha = source[index + 3];
            if (alpha == 0 && transparent) continue;
            if (alpha != 255) { error = SkinImageError.Transparency; return result; }
            var color = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(index));
            var normalized = Color15Bit.GetColorExpand(Color15Bit.GetColorCompress(color));
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(index), normalized);
        }
        return result;
    }
    private static int ColorCount(byte[] pixels)
    {
        var colors = new HashSet<int>();
        for (var i = 0; i < pixels.Length; i += 4) colors.Add(BinaryPrimitives.ReadInt32LittleEndian(pixels.AsSpan(i)));
        return colors.Count;
    }
    private static int CountTiles(byte[] pixels)
    {
        var colors = new Dictionary<int, byte>(); var tiles = new List<byte[]>();
        for (var tile = 0; tile < 768; tile++)
        {
            var indices = new byte[64];
            for (var p = 0; p < 64; p++)
            {
                var index = ((tile / 32 * 8 + p / 8) * Width) + tile % 32 * 8 + p % 8;
                var color = BinaryPrimitives.ReadInt32LittleEndian(pixels.AsSpan(index * 4));
                if (!colors.TryGetValue(color, out var choice)) colors[color] = choice = (byte)colors.Count;
                indices[p] = choice;
            }
            if (tiles.Any(existing => PaletteTile.GetRotationValue(indices, existing) != PaletteTileRotation.Invalid)) continue;
            tiles.Add(indices); if (tiles.Count > 255) return tiles.Count;
        }
        return tiles.Count;
    }
    private static (byte[], bool[]) ReadBackground()
    {
        using var stream = typeof(SkinImage5).Assembly.GetManifestResourceStream("PKHeX.Application.Resources.Gen5.DexBackground.bin")
            ?? throw new InvalidOperationException("Missing Pokédex background template.");
        using var reader = new BinaryReader(stream);
        var indices = new byte[Pixels]; var overrides = new bool[Pixels]; var position = 0;
        var pairs = reader.ReadUInt16();
        for (var i = 0; i < pairs; i++)
        {
            var count = reader.ReadByte(); var value = (byte)(reader.ReadByte() & 63);
            for (var j = 0; j < count; j++) indices[position++] = value;
        }
        if (position != Pixels) throw new InvalidDataException("Incomplete Pokédex background indices.");
        position = 0; pairs = reader.ReadUInt16();
        for (var i = 0; i < pairs; i++)
        {
            var count = reader.ReadUInt16(); var value = reader.ReadByte();
            for (var j = 0; j < count; j++)
                for (var bit = 0; bit < 8; bit++) overrides[position++] = (value & (1 << bit)) != 0;
        }
        if (position != Pixels) throw new InvalidDataException("Incomplete Pokédex background overrides.");
        return (indices, overrides);
    }
}
