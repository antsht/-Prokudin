using System.Diagnostics;

namespace Prokudin.Gui.Services;

public static class StartupExceptionReporter
{
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prokudin",
        "startup-error.log");

    public static void Report(Exception exception, string? logPath = null)
    {
        Trace.TraceError("Prokudin startup failed: {0}", exception);
        try
        {
            var path = logPath ?? DefaultLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (Exception reportException)
        {
            Trace.TraceError("Could not write Prokudin startup error report: {0}", reportException);
        }
    }
}
