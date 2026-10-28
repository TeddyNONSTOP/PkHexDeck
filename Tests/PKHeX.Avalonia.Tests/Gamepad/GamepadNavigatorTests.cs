using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PKHeX.Avalonia.Services;

namespace PKHeX.Avalonia.Tests.Gamepad;

/// <summary>What each controller press does to real controls, in a headless window.</summary>
public sealed class GamepadNavigatorTests
{
    private static Window Show(Control content, double width = 400, double height = 300)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IInputElement? Focused(TopLevel top) => top.FocusManager?.GetFocusedElement();

    private static bool Press(TopLevel top, GamepadButtons button)
    {
        var result = GamepadNavigator.Apply(top, button);
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    [AvaloniaFact]
    public void FirstPress_OnlyFocusesWithoutActivating()
    {
        var clicks = 0;
        var button = new Button { Content = "Go" };
        button.Click += (_, _) => clicks++;
        var window = Show(new StackPanel { Children = { button } });

        Press(window, GamepadButtons.Accept);

        Assert.Same(button, Focused(window));
        Assert.Equal(0, clicks);
        window.Close();
    }

    [AvaloniaFact]
    public void Dpad_MovesFocusToTheNearestControlInThatDirection()
    {
        var a = new Button { Content = "a" }; var b = new Button { Content = "b" };
        var c = new Button { Content = "c" }; var d = new Button { Content = "d" };
        var grid = new Grid { ColumnDefinitions = new("100,100"), RowDefinitions = new("40,40") };
        Grid.SetColumn(b, 1); Grid.SetRow(c, 1); Grid.SetRow(d, 1); Grid.SetColumn(d, 1);
        grid.Children.AddRange([a, b, c, d]);
        var window = Show(grid);
        a.Focus();

        Press(window, GamepadButtons.Down);
        Assert.Same(c, Focused(window));
        Press(window, GamepadButtons.Right);
        Assert.Same(d, Focused(window));
        Press(window, GamepadButtons.Up);
        Assert.Same(b, Focused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Dpad_LeavesATextBoxInsteadOfMovingItsCaret()
    {
        var text = new TextBox { Text = "Pikachu", Width = 150 };
        var below = new Button { Content = "below" };
        var right = new Button { Content = "right" };
        var grid = new Grid { ColumnDefinitions = new("160,100"), RowDefinitions = new("40,40") };
        Grid.SetColumn(right, 1); Grid.SetRow(below, 1);
        grid.Children.AddRange([text, right, below]);
        var window = Show(grid);
        text.Focus();

        Press(window, GamepadButtons.Right);
        Assert.Same(right, Focused(window));

        text.Focus();
        Press(window, GamepadButtons.Down);
        Assert.Same(below, Focused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Accept_ClicksAButtonAndTogglesACheckBox()
    {
        var clicks = 0;
        var button = new Button { Content = "Go" };
        button.Click += (_, _) => clicks++;
        var check = new CheckBox { Content = "Shiny" };
        var window = Show(new StackPanel { Children = { button, check } });

        button.Focus();
        Press(window, GamepadButtons.Accept);
        Assert.Equal(1, clicks);

        check.Focus();
        Press(window, GamepadButtons.Accept);
        Assert.True(check.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void Accept_OpensAComboBox_BackClosesIt()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Bulbasaur", "Ivysaur", "Venusaur" }, SelectedIndex = 0, Width = 150 };
        var window = Show(new StackPanel { Children = { combo } });
        combo.Focus();

        Press(window, GamepadButtons.Accept);
        Assert.True(combo.IsDropDownOpen);

        Press(window, GamepadButtons.Back);
        Assert.False(combo.IsDropDownOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Back_ClosesADialogWhenNothingIsOpen()
    {
        var owner = Show(new Button { Content = "main" });
        var dialogButton = new Button { Content = "OK" };
        var dialog = new Window { Content = dialogButton, Width = 200, Height = 100 };
        dialog.Show(owner);
        Dispatcher.UIThread.RunJobs();
        dialogButton.Focus();

        Press(dialog, GamepadButtons.Back);

        Assert.False(dialog.IsVisible);
        Assert.True(owner.IsVisible);

        // The main window itself is never closed by B.
        ((Button)owner.Content!).Focus();
        Press(owner, GamepadButtons.Back);
        Assert.True(owner.IsVisible);
        owner.Close();
    }

    [AvaloniaFact]
    public void Shoulders_SwitchTabsAndWrap()
    {
        var first = new Button { Content = "1" };
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Main", Content = first },
                new TabItem { Header = "Met", Content = new Button { Content = "2" } },
                new TabItem { Header = "Stats", Content = new Button { Content = "3" } },
            },
        };
        var window = Show(tabs, 500, 300);
        first.Focus();

        Press(window, GamepadButtons.NextTab);
        Assert.Equal(1, tabs.SelectedIndex);
        Press(window, GamepadButtons.PreviousTab);
        Press(window, GamepadButtons.PreviousTab);
        Assert.Equal(2, tabs.SelectedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void Triggers_ChangeNumbersAndComboSelections()
    {
        var numeric = new NumericUpDown { Minimum = 0, Maximum = 31, Value = 30, Increment = 1, Width = 120 };
        var combo = new ComboBox { ItemsSource = new[] { "Hardy", "Lonely", "Brave" }, SelectedIndex = 0, Width = 120 };
        var window = Show(new StackPanel { Children = { numeric, combo } });

        numeric.Focus();
        Press(window, GamepadButtons.Increase);
        Press(window, GamepadButtons.Increase); // clamped at the maximum
        Assert.Equal(31, numeric.Value);
        Press(window, GamepadButtons.Decrease);
        Assert.Equal(30, numeric.Value);

        combo.Focus();
        Press(window, GamepadButtons.Increase);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Menu_OpensTheContextMenuOfTheFocusedControlOrItsParent()
    {
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "View" }, new MenuItem { Header = "Delete" } } };
        var slot = new Button { Content = "slot" };
        var host = new Border { ContextMenu = menu, Child = slot };
        var window = Show(host);
        slot.Focus();

        Assert.True(Press(window, GamepadButtons.Menu));
        Assert.True(menu.IsOpen);
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void RightStick_ScrollsTheFocusedScrollViewer()
    {
        var panel = new StackPanel();
        for (var i = 0; i < 40; i++)
            panel.Children.Add(new Button { Content = $"Row {i}", Height = 30 });
        var viewer = new ScrollViewer { Content = panel, Height = 200 };
        var window = Show(viewer, 300, 200);
        ((Button)panel.Children[0]).Focus();

        Press(window, GamepadButtons.ScrollDown);
        Assert.True(viewer.Offset.Y > 0);
        Press(window, GamepadButtons.ScrollUp);
        Assert.Equal(0, viewer.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void Start_OpensTheMainMenu()
    {
        var file = new MenuItem { Header = "File", ItemsSource = new[] { new MenuItem { Header = "Open" } } };
        var menu = new Menu { ItemsSource = new[] { file } };
        var body = new Button { Content = "body" };
        var window = Show(new DockPanel { Children = { menu, body } });
        DockPanel.SetDock(menu, Dock.Top);
        body.Focus();

        Assert.True(Press(window, GamepadButtons.MainMenu));
        Assert.True(file.IsSubMenuOpen);
        file.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Settings_SaveStoresTheControllerMode()
    {
        var settings = new AppSettings();
        var vm = new PKHeX.Presentation.ViewModels.SettingsViewModel(settings, new FakeSettingsStore(),
            new Moq.Mock<IThemeService>().Object, new Moq.Mock<IUiDensityService>().Object,
            new LanguageService(), UpdateTestDoubles.Coordinator());

        Assert.Equal(GamepadInputMode.Automatic, vm.SelectedGamepadMode);
        Assert.Equal(Enum.GetValues<GamepadInputMode>(), vm.GamepadModes.ToArray());

        vm.SelectedGamepadMode = GamepadInputMode.Off;
        vm.SaveCommand.Execute(null);
        Assert.Equal(GamepadInputMode.Off, settings.Gamepad.Mode);
    }
}
