using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;
using Xunit;

namespace PKHeX.Avalonia.Tests;

public class JoinAvenueEditorHeadlessTests
{
    [AvaloniaFact]
    public void JoinAvenueEditor_VisitorShopTupleControls_RoundTripThroughView()
    {
        var sav = new SAV5B2W2();
        var live = sav.JoinAvenue.Self;
        live.ShopType = 208; // ((2 * 8) + 4) * 10 + 7 + 1
        live.DesiredShopType = 320; // ((3 * 8) + 7) * 10 + 9 + 1

        var vm = new JoinAvenueEditorViewModel(sav);
        var view = new JoinAvenueEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        window.Show();
        Pump(window);

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1; // Self
        Pump(window);

        var activeType = FindCombo(view, "Shop Type");
        var desiredType = FindCombo(view, "Desired Shop Type");
        var activeLevel = FindNumeric(view, "Shop Type Level");
        var desiredLevel = FindNumeric(view, "Desired Shop Level");
        var activeVersion = FindNumeric(view, "Shop Type Version");
        var desiredVersion = FindNumeric(view, "Desired Shop Version");

        Assert.Equal((int)JoinAvenueShopType5.Dojo, activeType.SelectedValue);
        Assert.Equal((int)JoinAvenueShopType5.Cafe, desiredType.SelectedValue);
        Assert.Equal(7, activeLevel.Value);
        Assert.Equal(9, desiredLevel.Value);
        Assert.Equal(2, activeVersion.Value);
        Assert.Equal(3, desiredVersion.Value);

        activeType.SelectedValue = (int)JoinAvenueShopType5.Raffle;
        activeLevel.Value = 4;
        activeVersion.Value = 1;
        desiredType.SelectedValue = -1;
        desiredLevel.Value = 0;
        desiredVersion.Value = 0;
        Pump(window);

        Assert.NotNull(live.ShopTypeTuple);
        Assert.Equal((byte)1, live.ShopTypeTuple!.Value.Version);
        Assert.Equal(JoinAvenueShopType5.Raffle, live.ShopTypeTuple.Value.Type);
        Assert.Equal((byte)4, live.ShopTypeTuple.Value.Rank);
        Assert.Null(live.DesiredShopTypeTuple);

        window.Close();
    }

    private static ComboBox FindCombo(Control root, string automationName) =>
        root.GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(control => global::Avalonia.Automation.AutomationProperties.GetName(control) == automationName);

    private static NumericUpDown FindNumeric(Control root, string automationName) =>
        root.GetVisualDescendants()
            .OfType<NumericUpDown>()
            .Single(control => global::Avalonia.Automation.AutomationProperties.GetName(control) == automationName);

    private static void Pump(Window window)
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
