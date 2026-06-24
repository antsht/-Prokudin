namespace Prokudin.Gui.ViewModels;

public readonly record struct ImageSelectionRect(int X, int Y, int Width, int Height)
{
    public static ImageSelectionRect Empty => default;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static ImageSelectionRect FromPoints(double x0, double y0, double x1, double y1, bool forceSquare = false)
    {
        if (forceSquare)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (size < 1)
            {
                size = 1;
            }

            var signX = dx >= 0 ? 1 : -1;
            var signY = dy >= 0 ? 1 : -1;
            x1 = x0 + (signX * size);
            y1 = y0 + (signY * size);
        }

        var left = (int)Math.Floor(Math.Min(x0, x1));
        var top = (int)Math.Floor(Math.Min(y0, y1));
        var right = (int)Math.Ceiling(Math.Max(x0, x1));
        var bottom = (int)Math.Ceiling(Math.Max(y0, y1));
        return new ImageSelectionRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public ImageSelectionRect Clamp(int imageWidth, int imageHeight)
    {
        if (IsEmpty || imageWidth <= 0 || imageHeight <= 0)
        {
            return Empty;
        }

        var x = Math.Clamp(X, 0, imageWidth - 1);
        var y = Math.Clamp(Y, 0, imageHeight - 1);
        var width = Math.Clamp(Width, 1, imageWidth - x);
        var height = Math.Clamp(Height, 1, imageHeight - y);
        return new ImageSelectionRect(x, y, width, height);
    }
}
