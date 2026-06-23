using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

internal static class HealingTileMerger
{
    public static void ApplyComponent(
        ImageBuffer result,
        Mat componentMask,
        float[] values,
        float featherSigma,
        int width,
        int height)
    {
        using var softMask = HealingMaskUtils.BuildSoftMask(componentMask, featherSigma);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var maskValue = softMask.At<float>(y, x);
                if (maskValue <= 0.0f)
                {
                    continue;
                }

                var index = (y * width) + x;
                var original = result.GetNormalized(index);
                var blended = (original * (1.0f - maskValue)) + (values[index] * maskValue);
                result.SetNormalized(index, blended);
            }
        }
    }
}
