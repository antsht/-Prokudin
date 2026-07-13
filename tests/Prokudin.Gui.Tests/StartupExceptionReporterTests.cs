using FluentAssertions;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class StartupExceptionReporterTests
{
    [Fact]
    public void Report_WritesExceptionDetailsToConfiguredLog()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-startup-{Guid.NewGuid():N}");
        var logPath = Path.Combine(folder, "startup-error.log");
        try
        {
            StartupExceptionReporter.Report(new InvalidOperationException("Welcome failed."), logPath);

            File.ReadAllText(logPath).Should().Contain("Welcome failed.");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
