namespace PKHeX.Presentation.ViewModels;

/// <summary>Common capability boundary for flat and game-aware event workspaces.</summary>
public abstract class EventEditorViewModel : ViewModelBase
{
    public abstract bool IsSupported { get; }
}
