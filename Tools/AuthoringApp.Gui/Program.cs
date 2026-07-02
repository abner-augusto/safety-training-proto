using Avalonia;

namespace SafetyProto.AuthoringApp.Gui;

internal static class Program
{
    // Avalonia desktop entry point. Keep it minimal and free of app logic so the visual
    // designer and the runtime share the same AppBuilder configuration.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
