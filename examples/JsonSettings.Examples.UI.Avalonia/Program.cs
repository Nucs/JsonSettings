using Avalonia;

namespace Nucs.JsonSettings.Examples.UI.Avalonia;

public static class Program {
    // Avalonia configuration and the settings file must both stay off this thread until
    // AppMain runs: the framework initializes rendering per platform, and DemoSettings is
    // deliberately Lazy so first file access happens after the app model exists.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Also used by the previewer/designer tooling; keep the signature.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
