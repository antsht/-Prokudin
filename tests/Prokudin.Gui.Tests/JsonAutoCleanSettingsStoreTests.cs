using FluentAssertions;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class JsonAutoCleanSettingsStoreTests
{
    [Fact]
    public void RoundTrip_PreservesQualityMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"autoclean-{Guid.NewGuid():N}.json");
        var store = new JsonAutoCleanSettingsStore(path);
        store.Save(new AutoCleanSettingsSnapshot(AutoCleanQualityMode.Balanced));
        store.Load().QualityMode.Should().Be(AutoCleanQualityMode.Balanced);
        File.Delete(path);
    }

    [Fact]
    public void RoundTrip_PreservesHealSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"autoclean-{Guid.NewGuid():N}.json");
        var store = new JsonAutoCleanSettingsStore(path);
        var settings = new AutoCleanSettingsSnapshot(
            QualityMode: AutoCleanQualityMode.Fast,
            Sensitivity: 72,
            PatchRadius: 5,
            SearchRadius: 64,
            ShowHealMaskOverlay: false);
        store.Save(settings);
        store.Load().Should().BeEquivalentTo(settings);
        File.Delete(path);
    }
}
