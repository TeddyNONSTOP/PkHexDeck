using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PKHeX.Avalonia.Services;

/// <summary>Plays a short WAV using the platform's own audio facility when available.</summary>
public sealed class PlatformAudioPlaybackService : IAudioPlaybackService
{
    private const uint SndSync = 0x0000;
    private const uint SndNodefault = 0x0002;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    public bool IsAvailable => OperatingSystem.IsWindows() || ResolvePlayer() is not null;

    public async Task<bool> PlayWavAsync(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (OperatingSystem.IsWindows())
            return await Task.Run(() => PlayWindows(wav));

        var player = ResolvePlayer();
        if (player is null) return false;
        var path = Path.Combine(Path.GetTempPath(), $"pkhex-chatter-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(path, wav);
            var start = new ProcessStartInfo(player)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(path); }
            catch { /* The short-lived playback file is best-effort cleanup. */ }
        }
    }

    private static bool PlayWindows(byte[] wav)
    {
        var pin = GCHandle.Alloc(wav, GCHandleType.Pinned);
        try
        {
            return PlaySound(pin.AddrOfPinnedObject(), IntPtr.Zero, SndSync | SndMemory | SndNodefault);
        }
        finally
        {
            pin.Free();
        }
    }

    private static string? ResolvePlayer()
    {
        if (OperatingSystem.IsMacOS())
            return File.Exists("/usr/bin/afplay") ? "/usr/bin/afplay" : null;
        if (!OperatingSystem.IsLinux()) return null;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "aplay", "paplay" })
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
