using FluentAssertions;
using OpenCvSharp;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class HealingTileMergerTests
{
    [Fact]
    public void ApplyComponent_AppliesFeatheredComponentValues()
    {
        var result = ImageBuffer.Filled(4, 4, 0.2f);
        using var component = new Mat(4, 4, MatType.CV_8UC1, Scalar.Black);
        component.Set(1, 1, (byte)255);
        var values = new float[16];
        values[5] = 0.9f;

        HealingTileMerger.ApplyComponent(result, component, values, featherSigma: 0f, width: 4, height: 4);

        result.GetNormalized(5).Should().BeApproximately(0.9f, 0.001f);
    }
}
