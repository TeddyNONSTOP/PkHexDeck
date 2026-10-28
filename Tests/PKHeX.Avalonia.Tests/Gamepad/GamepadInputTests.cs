using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PKHeX.Infrastructure.Gamepad;

namespace PKHeX.Avalonia.Tests.Gamepad;

/// <summary>Controller support: press/repeat timing, when the controller is read, and session detection.</summary>
public sealed class GamepadInputTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Repeater_ButtonFiresOncePerPress()
    {
        var repeater = new GamepadRepeater();

        Assert.Equal([GamepadButtons.Accept], repeater.Update(GamepadButtons.Accept, Ms(0)));
        Assert.Empty(repeater.Update(GamepadButtons.Accept, Ms(2000)));
        Assert.Empty(repeater.Update(GamepadButtons.None, Ms(2016)));
        Assert.Equal([GamepadButtons.Accept], repeater.Update(GamepadButtons.Accept, Ms(2032)));
    }

    [Fact]
    public void Repeater_HeldDirectionRepeatsAfterDelay()
    {
        var repeater = new GamepadRepeater();

        Assert.Equal([GamepadButtons.Down], repeater.Update(GamepadButtons.Down, Ms(0)));
        Assert.Empty(repeater.Update(GamepadButtons.Down, Ms(399)));
        Assert.Equal([GamepadButtons.Down], repeater.Update(GamepadButtons.Down, Ms(400)));
        Assert.Empty(repeater.Update(GamepadButtons.Down, Ms(450)));
        Assert.Equal([GamepadButtons.Down], repeater.Update(GamepadButtons.Down, Ms(490)));

        // Releasing resets the delay.
        repeater.Update(GamepadButtons.None, Ms(500));
        Assert.Equal([GamepadButtons.Down], repeater.Update(GamepadButtons.Down, Ms(516)));
        Assert.Empty(repeater.Update(GamepadButtons.Down, Ms(700)));
    }

    [Fact]
    public void Repeater_SimultaneousPressesAllFire()
    {
        var repeater = new GamepadRepeater();
        var pressed = repeater.Update(GamepadButtons.Up | GamepadButtons.NextTab, Ms(0));

        Assert.Equal(2, pressed.Count);
        Assert.Contains(GamepadButtons.Up, pressed);
        Assert.Contains(GamepadButtons.NextTab, pressed);
    }

    private sealed class FakeEnvironment(bool gameMode, bool steam) : IGamepadEnvironment
    {
        public bool IsGameMode => gameMode;
        public bool IsSteamRunning => steam;
    }

    [Theory]
    [InlineData(GamepadInputMode.Automatic, true, true, true)]    // Game Mode: Steam hands the app a gamepad
    [InlineData(GamepadInputMode.Automatic, false, true, false)]  // Desktop Mode: Steam already sends keys
    [InlineData(GamepadInputMode.Automatic, false, false, true)]  // no Steam: nobody else maps the pad
    [InlineData(GamepadInputMode.On, false, true, true)]
    [InlineData(GamepadInputMode.Off, true, false, false)]
    public void Policy_DecidesWhenToReadTheController(GamepadInputMode mode, bool gameMode, bool steam, bool expected)
        => Assert.Equal(expected, GamepadPolicy.ShouldReadController(mode, new FakeEnvironment(gameMode, steam)));

    [Theory]
    [InlineData("gamescope", null, true)]
    [InlineData("KDE", null, false)]
    [InlineData(null, "gamescope-0", true)]
    [InlineData(null, null, false)]
    public void Environment_DetectsGameMode(string? desktop, string? gamescopeDisplay, bool expected)
    {
        var env = new GamepadEnvironment(name => name switch
        {
            "XDG_CURRENT_DESKTOP" => desktop,
            "GAMESCOPE_WAYLAND_DISPLAY" => gamescopeDisplay,
            _ => null,
        }, Path.GetTempPath());

        Assert.Equal(expected, env.IsGameMode);
    }

    [Fact]
    public void Environment_DetectsSteamFromItsPidFile()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var home = Path.Combine(Path.GetTempPath(), "pkhex-gamepad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".steam"));
        try
        {
            var env = new GamepadEnvironment(_ => null, home);
            Assert.False(env.IsSteamRunning); // no pid file

            // A live process (this test run) stands in for Steam.
            File.WriteAllText(Path.Combine(home, ".steam", "steam.pid"), Environment.ProcessId.ToString());
            Assert.True(env.IsSteamRunning);

            File.WriteAllText(Path.Combine(home, ".steam", "steam.pid"), "999999999");
            Assert.False(env.IsSteamRunning);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
