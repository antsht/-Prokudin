using Avalonia;
using Zafiro.Avalonia.Icons;

namespace Prokudin.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        _ = typeof(IconProviderExtensions);

        IconControlProviderRegistry.Register(new OptrisIconControlProvider(), asDefault: true);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
