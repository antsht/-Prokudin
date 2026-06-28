using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Prokudin.Gui.Tests;

internal static class AvaloniaTestHost
{
    private static readonly object Sync = new();
    private static Thread? uiThread;
    private static bool initialized;

    internal static T Invoke<T>(Func<T> action)
    {
        EnsureInitialized();
        return Dispatcher.UIThread.Invoke(action);
    }

    internal static void Invoke(Action action) => Invoke(() =>
    {
        action();
        return true;
    });

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        lock (Sync)
        {
            if (initialized)
            {
                return;
            }

            var ready = new ManualResetEventSlim(false);
            uiThread = new Thread(() =>
            {
                AppBuilder.Configure<Prokudin.Gui.App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                ready.Set();
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            });

            if (OperatingSystem.IsWindows())
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }

            uiThread.IsBackground = true;
            uiThread.Start();
            ready.Wait();
            initialized = true;
        }
    }
}
