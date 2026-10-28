using Avalonia.Controls;
using Avalonia.Input;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Views;

public partial class NuzlockeReportView : UserControl
{
    public NuzlockeReportView()
    {
        InitializeComponent();
    }

    // Double-clicking a row shows that location's encounter in its box, the same as pressing Enter.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NuzlockeReportViewModel vm && vm.ShowSelectedCommand.CanExecute(null))
            vm.ShowSelectedCommand.Execute(null);
    }
}
