namespace PKHeX.Application.Abstractions;

/// <summary>Logical controller inputs the app reacts to, independent of the controller model.</summary>
[Flags]
public enum GamepadButtons
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    /// <summary>Bottom face button (Xbox A, Steam Deck A): activate.</summary>
    Accept = 1 << 4,
    /// <summary>Right face button (Xbox B): back / close.</summary>
    Back = 1 << 5,
    /// <summary>Left face button (Xbox X): context menu.</summary>
    Menu = 1 << 6,
    /// <summary>Top face button (Xbox Y): toggle / select.</summary>
    Toggle = 1 << 7,
    PreviousTab = 1 << 8,
    NextTab = 1 << 9,
    Decrease = 1 << 10,
    Increase = 1 << 11,
    ScrollUp = 1 << 12,
    ScrollDown = 1 << 13,
    /// <summary>Start: open the main menu bar.</summary>
    MainMenu = 1 << 14,
    /// <summary>Select / View: move to the next group of controls (like Tab).</summary>
    NextGroup = 1 << 15,
}

/// <summary>How controller input is handled. Stored in settings.</summary>
public enum GamepadInputMode
{
    /// <summary>
    /// Read the controller directly in Steam's Game Mode or when Steam isn't running. In Desktop Mode
    /// Steam already turns the controller into keyboard input, so reading it too would act twice.
    /// </summary>
    Automatic,
    On,
    Off,
}

/// <summary>
/// A connected game controller, polled outside the UI thread. <see cref="Pressed"/> fires once per
/// press and repeatedly while a direction, trigger or scroll input is held.
/// </summary>
public interface IGamepadInputSource : IDisposable
{
    event Action<GamepadButtons>? Pressed;

    bool IsRunning { get; }

    /// <summary>Starts polling; false when no controller library is available on this system.</summary>
    bool TryStart();

    void Stop();
}

/// <summary>Facts about the session that decide whether <see cref="GamepadInputMode.Automatic"/> reads the controller.</summary>
public interface IGamepadEnvironment
{
    /// <summary>Running inside Steam's Game Mode (gamescope) session.</summary>
    bool IsGameMode { get; }

    /// <summary>The Steam client is running, so it may be mapping the controller to keyboard and mouse.</summary>
    bool IsSteamRunning { get; }
}
