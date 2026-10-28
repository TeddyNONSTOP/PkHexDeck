using System.Buffers.Binary;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>Converts the 1,000-byte packed 4-bit Chatot recording to a mono 8-bit WAV.</summary>
public static class ChatterAudioCodec
{
    public const int SampleRate = 2000;
    public const int WavHeaderSize = 44;

    public static byte[] ToWav(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length != IChatter.SIZE_PCM)
            throw new ArgumentException($"Expected {IChatter.SIZE_PCM} PCM bytes.", nameof(pcm));

        var sampleCount = pcm.Length * 2;
        var wav = new byte[WavHeaderSize + sampleCount];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(wav.Length - 8));
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        "fmt "u8.CopyTo(wav.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), 1); // mono
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), SampleRate); // 8-bit mono byte rate
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 8);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)sampleCount);

        var offset = WavHeaderSize;
        foreach (var value in pcm)
        {
            wav[offset++] = (byte)((value & 0x0F) << 4);
            wav[offset++] = (byte)(value & 0xF0);
        }
        return wav;
    }
}
