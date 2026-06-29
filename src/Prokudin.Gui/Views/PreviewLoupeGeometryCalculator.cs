using Avalonia;

namespace Prokudin.Gui.Views;

internal static class PreviewLoupeGeometryCalculator
{
    public const int DefaultMagnification = 4;

    public const int DefaultSourceSize = 48;

    public static PixelRect ComputeSourceRect(
        int imageWidth,
        int imageHeight,
        double centerX,
        double centerY,
        int sourceSize = DefaultSourceSize)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return default;
        }

        var clampedSize = Math.Min(sourceSize, Math.Min(imageWidth, imageHeight));
        var half = clampedSize / 2;
        var x = (int)Math.Floor(centerX) - half;
        var y = (int)Math.Floor(centerY) - half;
        x = Math.Clamp(x, 0, Math.Max(0, imageWidth - clampedSize));
        y = Math.Clamp(y, 0, Math.Max(0, imageHeight - clampedSize));
        return new PixelRect(x, y, clampedSize, clampedSize);
    }

    public static Point ComputeLoupePosition(
        double cursorX,
        double cursorY,
        double panelWidth,
        double panelHeight,
        double hostWidth,
        double hostHeight,
        double offset = 20)
    {
        if (hostWidth <= 0 || hostHeight <= 0 || panelWidth <= 0 || panelHeight <= 0)
        {
            return default;
        }

        var left = cursorX + offset;
        var top = cursorY + offset;
        if (left + panelWidth > hostWidth)
        {
            left = cursorX - panelWidth - offset;
        }

        if (top + panelHeight > hostHeight)
        {
            top = cursorY - panelHeight - offset;
        }

        left = Math.Clamp(left, 0, Math.Max(0, hostWidth - panelWidth));
        top = Math.Clamp(top, 0, Math.Max(0, hostHeight - panelHeight));
        return new Point(left, top);
    }
}
