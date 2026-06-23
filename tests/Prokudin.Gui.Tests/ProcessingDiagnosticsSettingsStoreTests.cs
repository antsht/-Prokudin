using FluentAssertions;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class ProcessingDiagnosticsSettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-diagnostics-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProcessingDiagnosticsSettingsStore(path);
            var settings = new ProcessingDiagnosticsSettings(
                LogComputeBackends: true,
                LogPipelineStages: true,
                LogCpuParallel: false,
                LogTimings: true);

            store.Save(settings);
            store.Load().Should().Be(settings);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
