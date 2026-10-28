using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Views;

public partial class DonutEditor : UserControl
{
    public DonutEditor()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: 1 } && DataContext is DonutEditorViewModel { IsSupported: true }
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not DonutEditorViewModel vm || e.DataTransfer.TryGetFiles() is not { Length: 1 } files) return;
        if (files[0].TryGetLocalPath() is { } path) await vm.ImportPathAsync(path);
    }
}
