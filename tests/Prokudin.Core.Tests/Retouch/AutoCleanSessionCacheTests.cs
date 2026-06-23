using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class AutoCleanSessionCacheTests
{
    [Fact]
    public void Cache_StoresAndRetrievesNormalizedBuffers()
    {
        var cache = new AutoCleanSessionCache();
        var target = new float[] { 0.1f, 0.2f };
        cache.Store(target, [0.3f, 0.4f], [0.5f, 0.6f]);

        cache.TryGet(out var storedTarget, out var guide1, out var guide2).Should().BeTrue();
        storedTarget.Should().Equal(target);
        guide1.Should().Equal([0.3f, 0.4f]);
        guide2.Should().Equal([0.5f, 0.6f]);
    }

    [Fact]
    public void Clear_RemovesCachedBuffers()
    {
        var cache = new AutoCleanSessionCache();
        cache.Store([0.1f], [0.2f], [0.3f]);
        cache.Clear();
        cache.TryGet(out _, out _, out _).Should().BeFalse();
    }
}
