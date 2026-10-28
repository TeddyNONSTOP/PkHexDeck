using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace PKHeX.Testing;

internal static class HeadlessRenderSettling
{
    internal static async Task Settle(Window window, Func<bool>? settled = null)
    {
        int consecutive = 0;
        for (int i = 0; i < (settled is null ? 3 : 50); i++)
        {
            // Request a UI animation frame explicitly: ForceRenderTimerTick can be a no-op
            // when the headless render timer has no active subscription.
            window.RequestAnimationFrame(_ => { });
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            await Task.Delay(100);
            if (settled is not null)
            {
                consecutive = settled() ? consecutive + 1 : 0;
                if (consecutive == 2) break;
            }
        }
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
}
