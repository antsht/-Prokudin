using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

internal sealed class PatchHealerContext
{
    public PatchHealerContext(
        float[] target,
        float[]? guide1,
        float[]? guide2,
        int width,
        int height)
    {
        Target = target;
        Guide1 = guide1;
        Guide2 = guide2;
        Width = width;
        Height = height;
    }

    public float[] Target { get; }

    public float[]? Guide1 { get; }

    public float[]? Guide2 { get; }

    public int Width { get; }

    public int Height { get; }

    public static PatchHealerContext Create(ImageBuffer target, ImageBuffer? guide1, ImageBuffer? guide2)
    {
        var pixelCount = target.PixelCount;
        var targetValues = new float[pixelCount];
        target.CopyNormalizedTo(targetValues);

        float[]? guide1Values = null;
        if (guide1 is not null)
        {
            guide1Values = new float[pixelCount];
            guide1.CopyNormalizedTo(guide1Values);
        }

        float[]? guide2Values = null;
        if (guide2 is not null)
        {
            guide2Values = new float[pixelCount];
            guide2.CopyNormalizedTo(guide2Values);
        }

        return new PatchHealerContext(targetValues, guide1Values, guide2Values, target.Width, target.Height);
    }
}
