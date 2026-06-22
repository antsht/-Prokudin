using Prokudin.Core.Imaging;

namespace Prokudin.Core.Color;

public static class ColorCorrection
{
    public static RgbImageBuffer ApplyWhiteBalance(RgbImageBuffer rgb)
    {
        return ApplyNeutralRegionBalance(rgb);
    }

    public static RgbImageBuffer ApplyPipetteBalance(RgbImageBuffer rgb, int x, int y, int radius = 3)
    {
        x = Math.Clamp(x, 0, rgb.Width - 1);
        y = Math.Clamp(y, 0, rgb.Height - 1);
        radius = Math.Max(1, radius);

        var x0 = Math.Max(0, x - radius);
        var x1 = Math.Min(rgb.Width, x + radius + 1);
        var y0 = Math.Max(0, y - radius);
        var y1 = Math.Min(rgb.Height, y + radius + 1);
        var means = ChannelMeans(rgb, x0, y0, x1, y1);
        var target = means.Max();
        if (target < 1e-6f)
        {
            return rgb;
        }

        var output = rgb.Clone();
        for (var i = 0; i < output.Pixels.Length; i += 3)
        {
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] = Clamp01(output.Pixels[i + c] * target / (means[c] + 1e-6f));
            }
        }

        return output;
    }

    public static RgbImageBuffer ApplyTempTint(RgbImageBuffer rgb, int temperature, int tint)
    {
        if (temperature == 0 && tint == 0)
        {
            return rgb;
        }

        var temp = temperature / 100.0f;
        var tintValue = tint / 100.0f;
        var output = rgb.Clone();
        for (var i = 0; i < output.Pixels.Length; i += 3)
        {
            output.Pixels[i] = Clamp01(output.Pixels[i] * (1.0f + (0.25f * temp)));
            output.Pixels[i + 1] = Clamp01(output.Pixels[i + 1] * (1.0f + (0.15f * tintValue)));
            output.Pixels[i + 2] = Clamp01(output.Pixels[i + 2] * (1.0f - (0.25f * temp)));
        }

        return output;
    }

    public static RgbImageBuffer ApplyColorSettings(RgbImageBuffer rgb, ColorSettings settings)
    {
        var output = rgb;
        if (settings.PipetteActive && settings.PipetteX >= 0 && settings.PipetteY >= 0)
        {
            output = ApplyPipetteBalance(output, settings.PipetteX, settings.PipetteY, settings.PipetteRadius);
        }
        else if (settings.AutoWhiteBalance)
        {
            output = ApplyWhiteBalance(output);
        }

        return settings.Temperature != 0 || settings.Tint != 0
            ? ApplyTempTint(output, settings.Temperature, settings.Tint)
            : output;
    }

    public static RgbImageBuffer ApplyGentleLevels(RgbImageBuffer rgb, float lowPercent = 1.0f, float highPercent = 99.0f, float maxGain = 1.3f)
    {
        var output = rgb.Clone();
        for (var c = 0; c < 3; c++)
        {
            var values = new float[rgb.Width * rgb.Height];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = output.Pixels[(i * 3) + c];
            }

            Array.Sort(values);
            var low = PercentileSorted(values, lowPercent);
            var high = PercentileSorted(values, highPercent);
            if (high - low < 1e-4f)
            {
                continue;
            }

            var gain = Math.Min(maxGain, 1.0f / (high - low));
            for (var i = c; i < output.Pixels.Length; i += 3)
            {
                output.Pixels[i] = Clamp01((output.Pixels[i] - low) * gain);
            }
        }

        return output;
    }

    private static RgbImageBuffer ApplyNeutralRegionBalance(RgbImageBuffer rgb, float brightnessPercent = 85.0f, float varianceThreshold = 0.08f)
    {
        var brightness = new float[rgb.Width * rgb.Height];
        for (var i = 0; i < brightness.Length; i++)
        {
            brightness[i] = (rgb.Pixels[i * 3] + rgb.Pixels[(i * 3) + 1] + rgb.Pixels[(i * 3) + 2]) / 3.0f;
        }

        var sorted = (float[])brightness.Clone();
        Array.Sort(sorted);
        var threshold = PercentileSorted(sorted, brightnessPercent);
        var sums = new float[3];
        var count = 0;

        for (var i = 0; i < brightness.Length; i++)
        {
            var r = rgb.Pixels[i * 3];
            var g = rgb.Pixels[(i * 3) + 1];
            var b = rgb.Pixels[(i * 3) + 2];
            var spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (brightness[i] < threshold || spread >= varianceThreshold)
            {
                continue;
            }

            sums[0] += r;
            sums[1] += g;
            sums[2] += b;
            count++;
        }

        return count < 100 ? ApplyGrayWorldBalance(rgb) : ScaleChannelsToWhite(rgb, sums.Select(s => s / count).ToArray());
    }

    private static RgbImageBuffer ApplyGrayWorldBalance(RgbImageBuffer rgb)
    {
        var means = ChannelMeans(rgb, 0, 0, rgb.Width, rgb.Height);
        var target = means.Average();
        var output = rgb.Clone();
        for (var i = 0; i < output.Pixels.Length; i += 3)
        {
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] = Clamp01(output.Pixels[i + c] * target / (means[c] + 1e-6f));
            }
        }

        return output;
    }

    private static RgbImageBuffer ScaleChannelsToWhite(RgbImageBuffer rgb, IReadOnlyList<float> means)
    {
        var output = rgb.Clone();
        var max = 0.0f;
        for (var i = 0; i < output.Pixels.Length; i += 3)
        {
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] *= 1.0f / (means[c] + 1e-6f);
                max = Math.Max(max, output.Pixels[i + c]);
            }
        }

        max += 1e-6f;
        for (var i = 0; i < output.Pixels.Length; i++)
        {
            output.Pixels[i] = Clamp01(output.Pixels[i] / max);
        }

        return output;
    }

    private static float[] ChannelMeans(RgbImageBuffer rgb, int x0, int y0, int x1, int y1)
    {
        var sums = new float[3];
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    sums[c] += rgb[x, y, c];
                }

                count++;
            }
        }

        return sums.Select(sum => sum / Math.Max(1, count)).ToArray();
    }

    private static float PercentileSorted(IReadOnlyList<float> sorted, float percentile)
    {
        if (sorted.Count == 0)
        {
            return 0.0f;
        }

        var position = Math.Clamp(percentile, 0.0f, 100.0f) / 100.0f * (sorted.Count - 1);
        var lower = (int)MathF.Floor(position);
        var upper = (int)MathF.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var t = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * t);
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0.0f, 1.0f);
    }
}
