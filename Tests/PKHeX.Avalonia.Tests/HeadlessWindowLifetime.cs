using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace PKHeX.Avalonia.Tests;

/// <summary>Releases a test window even when an assertion fails, before the next UI test runs.</summary>
internal sealed class HeadlessWindowLifetime(Window window) : IDisposable
{
    public void Dispose()
    {
        window.DataContext = null;
        window.Close();
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
