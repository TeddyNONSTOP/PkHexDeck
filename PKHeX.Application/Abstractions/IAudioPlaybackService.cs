namespace PKHeX.Application.Abstractions;

/// <summary>Platform audio playback without UI-framework types in Presentation.</summary>
public interface IAudioPlaybackService
{
    bool IsAvailable { get; }
    Task<bool> PlayWavAsync(byte[] wav);
}
