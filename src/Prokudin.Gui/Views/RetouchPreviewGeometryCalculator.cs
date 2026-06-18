using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

internal readonly record struct RetouchPreviewGeometry(
    bool IsVisible,
    double CenterX,
    double CenterY,
    double BrushDiameter,
    double OuterDiameter)
{
    public static RetouchPreviewGeometry Hidden => default;
}

internal static class RetouchPreviewGeometryCalculator
{
    public static RetouchPreviewGeometry CalculateCursor(
        bool hasImage,
        PreviewInteractionMode interactionMode,
        double imageX,
        double imageY,
        int brushSize,
        int inpaintRadius,
        double scale,
        double offsetX,
        double offsetY)
    {
        if (!hasImage || interactionMode != PreviewInteractionMode.Retouch || scale <= 0)
        {
            return RetouchPreviewGeometry.Hidden;
        }

        var normalizedBrushSize = Math.Clamp(brushSize, 1, 200);
        var normalizedInpaintRadius = Math.Clamp(inpaintRadius, 1, 24);
        var brushDiameter = normalizedBrushSize * scale;
        var outerDiameter = (normalizedBrushSize + (normalizedInpaintRadius * 2)) * scale;

        return new RetouchPreviewGeometry(
            IsVisible: true,
            CenterX: offsetX + (imageX * scale),
            CenterY: offsetY + (imageY * scale),
            BrushDiameter: brushDiameter,
            OuterDiameter: outerDiameter);
    }
}
