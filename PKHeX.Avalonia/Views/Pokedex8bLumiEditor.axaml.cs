using Avalonia;
using Avalonia.Controls;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Views;

public partial class Pokedex8bLumiEditor : UserControl
{
    public Pokedex8bLumiEditor()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is Pokedex8bLumiEditorViewModel vm)
        {
            vm.SaveCurrent();
        }
    }
}
