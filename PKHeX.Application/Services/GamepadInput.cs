using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Services;

/// <summary>Decides whether the app should read the controller itself.</summary>
public static class GamepadPolicy
{
    public static bool ShouldReadController(GamepadInputMode mode, IGamepadEnvironment environment) => mode switch
    {
        GamepadInputMode.On => true,
        GamepadInputMode.Off => false,
        // Steam's Desktop layout already sends arrow keys, Enter and Escape; reading the
        // controller as well would make every press count twice.
        _ => environment.IsGameMode || !environment.IsSteamRunning,
    };
}

/// <summary>
/// Turns polled controller state into presses: one per press for buttons, and for held directions,
/// triggers and scroll a first repeat after <see cref="InitialDelay"/>, then every <see cref="RepeatInterval"/>.
/// </summary>
public sealed class GamepadRepeater
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(90);

    private const GamepadButtons Repeating = GamepadButtons.Up | GamepadButtons.Down | GamepadButtons.Left | GamepadButtons.Right
        | GamepadButtons.Decrease | GamepadButtons.Increase | GamepadButtons.ScrollUp | GamepadButtons.ScrollDown;

    private GamepadButtons _held;
    private readonly Dictionary<GamepadButtons, TimeSpan> _nextRepeat = [];

    public IReadOnlyList<GamepadButtons> Update(GamepadButtons state, TimeSpan now)
    {
        var pressed = new List<GamepadButtons>();
        foreach (var button in Enum.GetValues<GamepadButtons>())
        {
            if (button == GamepadButtons.None)
                continue;

            var isDown = state.HasFlag(button);
            var wasDown = _held.HasFlag(button);
            if (isDown && !wasDown)
            {
                pressed.Add(button);
                if ((Repeating & button) != 0)
                    _nextRepeat[button] = now + InitialDelay;
            }
            else if (isDown && _nextRepeat.TryGetValue(button, out var due) && now >= due)
            {
                pressed.Add(button);
                _nextRepeat[button] = now + RepeatInterval;
            }
            else if (!isDown)
            {
                _nextRepeat.Remove(button);
            }
        }
        _held = state;
        return pressed;
    }
}
