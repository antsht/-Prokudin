using System.Diagnostics;
using Avalonia;
using Zafiro.Avalonia.Icons;

namespace Prokudin.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                Trace.TraceError("Unhandled exception: {0}", ex);
            }
            else
            {
                Trace.TraceError("Unhandled exception: {0}", eventArgs.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Trace.TraceError("Unobserved task exception: {0}", eventArgs.Exception);
            eventArgs.SetObserved();
        };

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
