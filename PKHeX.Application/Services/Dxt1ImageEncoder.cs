using System.Buffers.Binary;
using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Services;

/// <summary>Encodes BGRA pixels as little-endian BC1/DXT1 blocks with one-bit alpha. No image framework dependency.</summary>
public static class Dxt1ImageEncoder
{
    public static byte[] Encode(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width is <= 0 or > 2048 || image.Height is <= 0 or > 2048 || image.Width % 4 != 0 || image.Height % 4 != 0 ||
            image.Bgra.Length != checked(image.Width * image.Height * 4))
            throw new ArgumentException("Invalid DXT1 image dimensions or pixel length.", nameof(image));
        var result = new byte[image.Width * image.Height / 2];
        Span<Color> pixels = stackalloc Color[16];
        Span<Color> palette = stackalloc Color[4];
        int offset = 0;
        for (int y = 0; y < image.Height; y += 4)
        for (int x = 0; x < image.Width; x += 4)
        {
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                int p = ((y + row) * image.Width + x + col) * 4;
                pixels[row * 4 + col] = new(image.Bgra[p + 2], image.Bgra[p + 1], image.Bgra[p], image.Bgra[p + 3] < 128);
            }
            int first = 0, second = 0, farthest = -1;
            for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
            {
                if (pixels[a].Transparent || pixels[b].Transparent) continue;
                int distance = Distance(pixels[a], pixels[b]);
                if (distance > farthest) { farthest = distance; first = a; second = b; }
            }
            bool transparent = false;
            for (int p = 0; p < 16; p++)
            {
                transparent |= pixels[p].Transparent;
                if (farthest == -1 && !pixels[p].Transparent) first = second = p;
            }
            ushort c0 = Quantize(pixels[first]), c1 = Quantize(pixels[second]);
            if (transparent ? c0 > c1 : c0 < c1) (c0, c1) = (c1, c0);
            // Keep the four-color opaque mode, including monochrome blocks.
            if (!transparent && c0 == c1) { if (c0 == 0) c0 = 1; else c1--; }
            palette[0] = Expand(c0); palette[1] = Expand(c1);
            palette[2] = Lerp(palette[0], palette[1], transparent ? 0.5f : 1f / 3f);
            palette[3] = Lerp(palette[0], palette[1], 2f / 3f);
            uint indices = 0;
            for (int p = 0; p < 16; p++)
            {
                if (pixels[p].Transparent) { indices |= 3u << (p * 2); continue; }
                int best = 0, error = int.MaxValue;
                for (int c = 0; c < (transparent ? 3 : 4); c++)
                {
                    int candidate = Distance(pixels[p], palette[c]);
                    if (candidate < error) { error = candidate; best = c; }
                }
                indices |= (uint)best << (p * 2);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), c0);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 2), c1);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4), indices);
            offset += 8;
        }
        return result;
    }

    private readonly record struct Color(int R, int G, int B, bool Transparent = false);
    private static int Distance(Color a, Color b) => 30 * Square(a.R - b.R) + 59 * Square(a.G - b.G) + 11 * Square(a.B - b.B);
    private static int Square(int value) => value * value;
    private static ushort Quantize(Color c) => (ushort)(((c.R * 31 + 127) / 255 << 11) | ((c.G * 63 + 127) / 255 << 5) | (c.B * 31 + 127) / 255);
    private static Color Expand(ushort value)
    {
        int r = value >> 11, g = value >> 5 & 63, b = value & 31;
        return new(r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2);
    }
    private static Color Lerp(Color a, Color b, float t) => new((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
