using Avalonia.Controls;
using Avalonia.Input;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Views;

/// <summary>The Bank window content: a save-independent box grid with withdraw, archive and backup actions.</summary>
public partial class BankView : UserControl
{
    public BankView()
    {
        InitializeComponent();
    }

    // Double-clicking an occupied slot withdraws it into the open save (same as the context menu).
    private void OnSlotDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Button { Tag: BankSlotData slot } && DataContext is BankViewModel vm && vm.WithdrawSlotCommand.CanExecute(slot))
            vm.WithdrawSlotCommand.Execute(slot);
    }
}
