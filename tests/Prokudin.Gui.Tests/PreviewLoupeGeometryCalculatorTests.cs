using Avalonia;
using FluentAssertions;
using Prokudin.Gui.Views;

namespace Prokudin.Gui.Tests;

public sealed class PreviewLoupeGeometryCalculatorTests
{
    [Fact]
    public void ComputeSourceRect_CentersOnImagePoint()
    {
        var rect = PreviewLoupeGeometryCalculator.ComputeSourceRect(400, 300, 120.5, 80.2);

        rect.X.Should().Be(96);
        rect.Y.Should().Be(56);
        rect.Width.Should().Be(48);
        rect.Height.Should().Be(48);
    }

    [Fact]
    public void ComputeSourceRect_ClampsNearEdges()
    {
        var rect = PreviewLoupeGeometryCalculator.ComputeSourceRect(64, 64, 2, 3);

        rect.X.Should().Be(0);
        rect.Y.Should().Be(0);
        rect.Width.Should().Be(48);
        rect.Height.Should().Be(48);
    }

    [Fact]
    public void ComputeLoupePosition_FlipsNearBottomRightEdge()
    {
        var position = PreviewLoupeGeometryCalculator.ComputeLoupePosition(
            580,
            420,
            panelWidth: 192,
            panelHeight: 192,
            hostWidth: 640,
            hostHeight: 480);

        position.X.Should().BeApproximately(368, 0.001);
        position.Y.Should().BeApproximately(208, 0.001);
    }
}
