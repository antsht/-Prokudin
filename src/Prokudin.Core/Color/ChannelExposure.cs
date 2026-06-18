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
        var pixels = new float[image.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Math.Clamp(image.Pixels[i] * gain, 0.0f, 1.0f);
        }

        return new ImageBuffer(image.Width, image.Height, pixels);
    }
}
