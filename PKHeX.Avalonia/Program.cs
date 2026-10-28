using Avalonia;

namespace PKHeX.Avalonia;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                WmClass = Environment.GetEnvironmentVariable("FLATPAK_ID") ?? "PKHeX.Avalonia",
                UseDBusFilePicker = true,
            })
            .WithInterFont()
            .LogToTrace();
}
