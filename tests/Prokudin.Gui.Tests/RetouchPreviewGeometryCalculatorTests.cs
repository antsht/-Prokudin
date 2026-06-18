using FluentAssertions;
using Prokudin.Gui.ViewModels;
using Prokudin.Gui.Views;

namespace Prokudin.Gui.Tests;

public sealed class RetouchPreviewGeometryCalculatorTests
{
    [Fact]
    public void CalculateCursor_UsesBrushDiameterAndInpaintRadius()
    {
        var geometry = RetouchPreviewGeometryCalculator.CalculateCursor(
            hasImage: true,
            interactionMode: PreviewInteractionMode.Retouch,
            imageX: 20,
            imageY: 30,
            brushSize: 12,
            inpaintRadius: 3,
            scale: 1.0,
            offsetX: 0,
            offsetY: 0);

        geometry.IsVisible.Should().BeTrue();
        geometry.CenterX.Should().Be(20);
        geometry.CenterY.Should().Be(30);
        geometry.BrushDiameter.Should().Be(12);
        geometry.OuterDiameter.Should().Be(18);
    }

    [Fact]
    public void CalculateCursor_ScalesCursorForFitToWindow()
    {
        var geometry = RetouchPreviewGeometryCalculator.CalculateCursor(
            hasImage: true,
            interactionMode: PreviewInteractionMode.Retouch,
            imageX: 20,
            imageY: 30,
            brushSize: 12,
            inpaintRadius: 3,
            scale: 0.5,
            offsetX: 10,
            offsetY: 5);

        geometry.IsVisible.Should().BeTrue();
        geometry.CenterX.Should().Be(20);
        geometry.CenterY.Should().Be(20);
        geometry.BrushDiameter.Should().Be(6);
        geometry.OuterDiameter.Should().Be(9);
    }

    [Theory]
    [InlineData(false, PreviewInteractionMode.Retouch)]
    [InlineData(true, PreviewInteractionMode.Selection)]
    public void CalculateCursor_HidesWhenImageOrRetouchModeIsUnavailable(
        bool hasImage,
        PreviewInteractionMode interactionMode)
    {
        var geometry = RetouchPreviewGeometryCalculator.CalculateCursor(
            hasImage,
            interactionMode,
            imageX: 20,
            imageY: 30,
            brushSize: 12,
            inpaintRadius: 3,
            scale: 1.0,
            offsetX: 0,
            offsetY: 0);

        geometry.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void CalculateStampGhost_MapsSourceAndDestinationAtOneToOne()
    {
        var geometry = RetouchPreviewGeometryCalculator.CalculateStampGhost(
            hasImage: true,
            interactionMode: PreviewInteractionMode.Retouch,
            retouchTool: RetouchTool.Stamp,
            sourceCenterX: 5,
            sourceCenterY: 7,
            destinationCenterX: 20,
            destinationCenterY: 30,
            brushSize: 12,
            scale: 1.0,
            offsetX: 0,
            offsetY: 0);

        geometry.IsVisible.Should().BeTrue();
        geometry.SourceLeft.Should().Be(-1);
        geometry.SourceTop.Should().Be(1);
        geometry.DestinationLeft.Should().Be(14);
        geometry.DestinationTop.Should().Be(24);
        geometry.Diameter.Should().Be(12);
    }

    [Fact]
    public void CalculateStampGhost_ScalesForFitToWindow()
    {
        var geometry = RetouchPreviewGeometryCalculator.CalculateStampGhost(
            hasImage: true,
            interactionMode: PreviewInteractionMode.Retouch,
            retouchTool: RetouchTool.Stamp,
            sourceCenterX: 5,
            sourceCenterY: 7,
            destinationCenterX: 20,
            destinationCenterY: 30,
            brushSize: 12,
            scale: 0.5,
            offsetX: 10,
            offsetY: 5);

        geometry.IsVisible.Should().BeTrue();
        geometry.SourceLeft.Should().BeApproximately(9.5, 0.001);
        geometry.SourceTop.Should().BeApproximately(5.5, 0.001);
        geometry.DestinationLeft.Should().BeApproximately(17, 0.001);
        geometry.DestinationTop.Should().BeApproximately(17, 0.001);
        geometry.Diameter.Should().Be(6);
    }
}
