using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;

namespace PKHeX.Avalonia.Services;

/// <summary>
/// Drives the UI from a game controller. Presses reach only the active window, and most of them are
/// turned into the keys the app already understands (arrows, Enter, Escape, Space, Tab), tagged as
/// gamepad input, so every control keeps its own keyboard behaviour. Controller-only actions (tab
/// switching, triggers changing values, the context-menu button) are handled here directly.
/// </summary>
/// <remarks>
/// Whether the controller is read at all follows <see cref="GamepadPolicy"/>; the decision is
/// re-checked every two seconds so a settings change or Steam starting/stopping takes effect live.
/// </remarks>
public sealed class GamepadNavigator : IDisposable
{
    private const double ScrollStep = 80;

    private readonly IGamepadInputSource _source;
    private readonly IGamepadEnvironment _environment;
    private readonly AppSettings _settings;
    private DispatcherTimer? _timer;

    public GamepadNavigator(IGamepadInputSource source, IGamepadEnvironment environment, AppSettings settings)
    {
        _source = source;
        _environment = environment;
        _settings = settings;
    }

    public void Start()
    {
        _source.Pressed += OnPressed;
        Evaluate();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Evaluate());
        _timer.Start();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _source.Pressed -= OnPressed;
        _source.Stop();
    }

    private void Evaluate()
    {
        var wanted = GamepadPolicy.ShouldReadController(_settings.Gamepad.Mode, _environment);
        if (wanted && !_source.IsRunning)
            _source.TryStart();
        else if (!wanted && _source.IsRunning)
            _source.Stop();
    }

    private void OnPressed(GamepadButtons button) => Dispatcher.UIThread.Post(() =>
    {
        if (!GamepadPolicy.ShouldReadController(_settings.Gamepad.Mode, _environment))
            return;
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        // Only the window the user is looking at reacts; presses meant for another app are ignored.
        var active = desktop.Windows.FirstOrDefault(w => w.IsActive);
        if (active is not null)
            Apply(active, button);
    });

    /// <summary>Performs one controller press on <paramref name="top"/>. Returns whether anything reacted.</summary>
    public static bool Apply(TopLevel top, GamepadButtons button)
    {
        var focused = top.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || !focused.IsEffectivelyVisible || focused.GetVisualRoot() is null)
        {
            // The first press only shows where focus is, so it never acts on something unseen.
            var first = KeyboardNavigationHandler.GetNext(top, NavigationDirection.Next) as Control;
            return first?.Focus(NavigationMethod.Directional) ?? false;
        }

        return button switch
        {
            GamepadButtons.Up => Move(focused, Key.Up, NavigationDirection.Up),
            GamepadButtons.Down => Move(focused, Key.Down, NavigationDirection.Down),
            GamepadButtons.Left => Move(focused, Key.Left, NavigationDirection.Left),
            GamepadButtons.Right => Move(focused, Key.Right, NavigationDirection.Right),
            GamepadButtons.Accept => Accept(focused),
            GamepadButtons.Back => Back(top, focused),
            GamepadButtons.Menu => OpenContextMenu(focused),
            GamepadButtons.Toggle => RaiseKey(focused, Key.Space),
            GamepadButtons.PreviousTab => SwitchTab(top, focused, -1),
            GamepadButtons.NextTab => SwitchTab(top, focused, +1),
            GamepadButtons.Decrease => Adjust(focused, -1),
            GamepadButtons.Increase => Adjust(focused, +1),
            GamepadButtons.ScrollUp => Scroll(top, focused, -ScrollStep),
            GamepadButtons.ScrollDown => Scroll(top, focused, +ScrollStep),
            GamepadButtons.MainMenu => OpenMainMenu(top),
            GamepadButtons.NextGroup => RaiseKey(focused, Key.Tab),
            _ => false,
        };
    }

    private static bool Move(Control focused, Key key, NavigationDirection direction)
    {
        // A text box would keep the arrows for its caret; with a controller the D-pad leaves it instead.
        if (focused is not TextBox && RaiseKey(focused, key))
            return true;
        var next = KeyboardNavigationHandler.GetNext(focused, direction) as Control;
        return next?.Focus(NavigationMethod.Directional) ?? false;
    }

    private static bool Accept(Control focused)
    {
        switch (FindAncestor<ComboBox>(focused))
        {
            case { IsDropDownOpen: false } combo:
                combo.IsDropDownOpen = true;
                return true;
        }
        if (FindAncestor<AutoCompleteBox>(focused) is { IsDropDownOpen: false } autoComplete)
        {
            autoComplete.IsDropDownOpen = true;
            return true;
        }
        if (focused is TextBox)
            return false; // typing happens through the on-screen keyboard (Steam + X)

        return RaiseKey(focused, Key.Enter) || RaiseKey(focused, Key.Space);
    }

    private static bool Back(TopLevel top, Control focused)
    {
        if (RaiseKey(focused, Key.Escape))
            return true;
        // Nothing open to dismiss: B closes a dialog the same way its window close button does.
        if (top is Window { Owner: not null } dialog)
        {
            dialog.Close();
            return true;
        }
        return false;
    }

    private static bool OpenContextMenu(Control focused)
    {
        for (Visual? v = focused; v is not null; v = v.GetVisualParent())
        {
            if (v is not Control control)
                continue;
            if (control.ContextMenu is { } menu)
            {
                menu.Open(control);
                return true;
            }
            if (control.ContextFlyout is { } flyout)
            {
                flyout.ShowAt(control);
                return true;
            }
        }
        return false;
    }

    private static bool SwitchTab(TopLevel top, Control focused, int step)
    {
        var tabs = FindAncestor<TabControl>(focused) ?? top.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t.IsEffectivelyVisible);
        if (tabs is null || tabs.ItemCount == 0)
            return false;

        var index = tabs.SelectedIndex;
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            index = (index + step + tabs.ItemCount) % tabs.ItemCount;
            if (tabs.ContainerFromIndex(index) is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } header)
            {
                tabs.SelectedIndex = index;
                header.Focus(NavigationMethod.Directional);
                return true;
            }
        }
        return false;
    }

    private static bool Adjust(Control focused, int step)
    {
        if (FindAncestor<NumericUpDown>(focused) is { } numeric)
        {
            var value = (numeric.Value ?? 0) + step * numeric.Increment;
            numeric.Value = Math.Clamp(value, numeric.Minimum, numeric.Maximum);
            return true;
        }
        if (FindAncestor<ComboBox>(focused) is { IsDropDownOpen: false, ItemCount: > 0 } combo)
        {
            combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + step, 0, combo.ItemCount - 1);
            return true;
        }
        if (FindAncestor<RangeBase>(focused) is { } range)
        {
            range.Value = Math.Clamp(range.Value + step * range.SmallChange, range.Minimum, range.Maximum);
            return true;
        }
        return false;
    }

    private static bool Scroll(TopLevel top, Control focused, double delta)
    {
        var viewer = FindAncestor<ScrollViewer>(focused)
            ?? top.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height);
        if (viewer is null)
            return false;
        var maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(viewer.Offset.Y + delta, 0, maxY));
        return true;
    }

    private static bool OpenMainMenu(TopLevel top)
    {
        var menu = top.GetVisualDescendants().OfType<Menu>().FirstOrDefault(m => m.IsEffectivelyVisible);
        if (menu?.ContainerFromIndex(0) is not MenuItem first)
            return false;
        first.Focus(NavigationMethod.Directional);
        first.Open();
        return true;
    }

    private static bool RaiseKey(Control target, Key key)
    {
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target, KeyDeviceType = KeyDeviceType.Gamepad };
        target.RaiseEvent(down);
        var up = new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = target, KeyDeviceType = KeyDeviceType.Gamepad };
        target.RaiseEvent(up);
        return down.Handled || up.Handled;
    }

    private static T? FindAncestor<T>(Visual start) where T : class
    {
        for (Visual? v = start; v is not null; v = v.GetVisualParent())
        {
            if (v is T match)
                return match;
        }
        return null;
    }
}
