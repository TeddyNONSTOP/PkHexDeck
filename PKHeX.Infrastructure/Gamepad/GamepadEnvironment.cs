using PKHeX.Application.Abstractions;

namespace PKHeX.Infrastructure.Gamepad;

/// <summary>Detects Steam's Game Mode session and a running Steam client on Linux.</summary>
public sealed class GamepadEnvironment : IGamepadEnvironment
{
    private readonly Func<string, string?> _getEnvironment;
    private readonly string _home;

    public GamepadEnvironment()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public GamepadEnvironment(Func<string, string?> getEnvironment, string home)
    {
        _getEnvironment = getEnvironment;
        _home = home;
    }

    // Game Mode runs apps under the gamescope compositor, which advertises itself in these variables.
    public bool IsGameMode
        => string.Equals(_getEnvironment("XDG_CURRENT_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(_getEnvironment("GAMESCOPE_WAYLAND_DISPLAY"));

    // Steam writes its process ID to ~/.steam/steam.pid; the process existing under /proc means it runs.
    public bool IsSteamRunning
    {
        get
        {
            if (!OperatingSystem.IsLinux())
                return false;
            try
            {
                var pidFile = Path.Combine(_home, ".steam", "steam.pid");
                if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) || pid <= 0)
                    return false;
                return Directory.Exists($"/proc/{pid}");
            }
            catch
            {
                return false;
            }
        }
    }
}
