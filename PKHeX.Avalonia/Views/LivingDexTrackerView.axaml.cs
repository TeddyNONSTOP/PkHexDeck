using Avalonia.Controls;
using Avalonia.Input;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Views;

public partial class LivingDexTrackerView : UserControl
{
    public LivingDexTrackerView()
    {
        InitializeComponent();
    }

    // Double-clicking a row shows that Pokémon in its box, the same as pressing Enter.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LivingDexTrackerViewModel vm && vm.ShowSelectedCommand.CanExecute(null))
            vm.ShowSelectedCommand.Execute(null);
    }
}
