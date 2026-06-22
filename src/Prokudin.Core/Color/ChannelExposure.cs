using Prokudin.Core.Imaging;

namespace Prokudin.Core.Color;

public static class ChannelExposure
{
    public static ImageBuffer Apply(ImageBuffer image, float stops)
    {
        if (Math.Abs(stops) < 0.001f)
        {
            return image;
        }

        var gain = MathF.Pow(2.0f, stops);
        var result = image.Clone();
        for (var i = 0; i < result.PixelCount; i++)
        {
            result.SetNormalized(i, Math.Clamp(image.GetNormalized(i) * gain, 0.0f, 1.0f));
        }

        return result;
    }
}
