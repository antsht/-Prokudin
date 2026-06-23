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
}
