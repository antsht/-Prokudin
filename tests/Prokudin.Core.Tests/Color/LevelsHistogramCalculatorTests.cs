using FluentAssertions;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Color;

public sealed class LevelsHistogramCalculatorTests
{
    [Fact]
    public void Calculate_UsesChannelInputBeforeMasterInputAndNormalizesEachDistribution()
    {
        var beforeChannelLevels = new RgbImageBuffer(
            2,
            1,
            [
                0.0f, 0.5f, 0.25f,
                1.0f, 0.5f, 0.75f,
            ]);
        var beforeMasterLevels = new RgbImageBuffer(
            2,
            1,
            [
                0.0f, 0.0f, 0.0f,
                1.0f, 1.0f, 1.0f,
            ]);

        var histogram = LevelsHistogramCalculator.Calculate(beforeChannelLevels, beforeMasterLevels, binCount: 4);

        histogram.Red.Should().Equal(1.0, 0.0, 0.0, 1.0);
        histogram.Green.Should().Equal(0.0, 0.0, 1.0, 0.0);
        histogram.Blue.Should().Equal(0.0, 1.0, 0.0, 1.0);
        histogram.Master.Should().Equal(1.0, 0.0, 0.0, 1.0);
    }

    [Theory]
    [InlineData(LevelsScope.Master)]
    [InlineData(LevelsScope.Red)]
    [InlineData(LevelsScope.Green)]
    [InlineData(LevelsScope.Blue)]
    public void ForScope_ReturnsTheMatchingDistribution(LevelsScope scope)
    {
        var histogram = new LevelsHistogramData(
            Master: [0.1],
            Red: [0.2],
            Green: [0.3],
            Blue: [0.4]);

        histogram.ForScope(scope).Single().Should().Be(scope switch
        {
            LevelsScope.Master => 0.1,
            LevelsScope.Red => 0.2,
            LevelsScope.Green => 0.3,
            LevelsScope.Blue => 0.4,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        });
    }
}
