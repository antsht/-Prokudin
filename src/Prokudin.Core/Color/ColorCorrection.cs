using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;

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
        PixelParallel.For(0, output.Pixels.Length / 3, pixel =>
        {
            var i = pixel * 3;
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] = Clamp01(output.Pixels[i + c] * target / (means[c] + 1e-6f));
            }
        });

        return output;
    }

    public static RgbImageBuffer ApplyTempTint(RgbImageBuffer rgb, int temperature, int tint)
    {
        if (temperature == 0 && tint == 0)
        {
            return rgb;
        }

        var temp = Math.Clamp(temperature, -100, 100) / 100.0f;
        var tintValue = Math.Clamp(tint, -100, 100) / 100.0f;
        var output = rgb.Clone();
        PixelParallel.For(0, output.Pixels.Length / 3, pixel =>
        {
            var i = pixel * 3;
            output.Pixels[i] = Clamp01(output.Pixels[i] * (1.0f + (0.25f * temp)));
            output.Pixels[i + 1] = Clamp01(output.Pixels[i + 1] * (1.0f + (0.15f * tintValue)));
            output.Pixels[i + 2] = Clamp01(output.Pixels[i + 2] * (1.0f - (0.25f * temp)));
        });

        return output;
    }

    public static RgbImageBuffer ApplyColorSettings(RgbImageBuffer rgb, ColorSettings settings)
    {
        var output = ApplyWhiteBalanceSource(rgb, settings.Source, settings.WhitePick);

        return settings.Temperature != 0 || settings.Tint != 0
            ? ApplyTempTint(output, settings.Temperature, settings.Tint)
            : output;
    }

    public static RgbImageBuffer ApplyWhiteBalanceSource(
        RgbImageBuffer rgb,
        WhiteBalanceSource source,
        WhitePick? whitePick = null) =>
        source switch
        {
            WhiteBalanceSource.Auto => ApplyWhiteBalance(rgb),
            WhiteBalanceSource.WhitePick when whitePick is not null =>
                ApplyPipetteBalance(rgb, whitePick.X, whitePick.Y, whitePick.EffectiveRadius),
            _ => rgb,
        };

    public static RgbImageBuffer ApplyGentleLevels(RgbImageBuffer rgb, float lowPercent = 1.0f, float highPercent = 99.0f, float maxGain = 1.3f) =>
        ApplyAutoMasterLevels(rgb, lowPercent, highPercent, maxGain);

    public static RgbImageBuffer ApplyAutoMasterLevels(
        RgbImageBuffer rgb,
        float lowPercent = 1.0f,
        float highPercent = 99.0f,
        float maxGain = 1.3f)
    {
        var luminance = new float[rgb.Width * rgb.Height];
        PixelParallel.For(0, luminance.Length, i =>
        {
            var pixel = i * 3;
            luminance[i] = Luminance(rgb.Pixels[pixel], rgb.Pixels[pixel + 1], rgb.Pixels[pixel + 2]);
        });

        Array.Sort(luminance);
        var black = PercentileSorted(luminance, lowPercent);
        var white = PercentileSorted(luminance, highPercent);
        if (white - black < 1e-4f)
        {
            return rgb;
        }

        var gain = Math.Min(maxGain, 1.0f / (white - black));
        return ApplyLevelCurve(rgb, black, gain, gamma: 1.0f);
    }

    public static RgbImageBuffer ApplyLevelsSettings(RgbImageBuffer rgb, LevelsSettings settings) =>
        settings.Mode switch
        {
            LevelsMode.Off => rgb,
            LevelsMode.Manual => ApplyManualLevelsAndGamma(
                rgb,
                Math.Clamp(settings.BlackPoint, 0f, 1f),
                Math.Clamp(settings.WhitePoint, settings.BlackPoint + 1e-4f, 1f),
                Math.Clamp(settings.Gamma, 0.1f, 5f)),
            _ => ApplyAutoMasterLevels(rgb, settings.AutoLowPercent, settings.AutoHighPercent, settings.AutoMaxGain),
        };

    public static RgbImageBuffer ApplyChannelLevels(RgbImageBuffer rgb, ChannelLevelsSettings settings)
    {
        if (settings.IsNeutral)
        {
            return rgb;
        }

        var output = rgb.Clone();
        for (var channel = 0; channel < 3; channel++)
        {
            var level = settings.ForIndex(channel);
            if (level.IsNeutral)
            {
                continue;
            }

            var black = Math.Clamp(level.BlackPoint, 0.0f, 1.0f);
            var white = Math.Clamp(level.WhitePoint, black + 1e-4f, 1.0f);
            var gamma = Math.Clamp(level.Gamma, 0.1f, 5.0f);
            var activeChannel = channel;
            PixelParallel.For(0, rgb.Width * rgb.Height, pixel =>
            {
                var index = (pixel * 3) + activeChannel;
                output.Pixels[index] = ApplyLevelCurve(output.Pixels[index], black, 1.0f / (white - black), gamma);
            });
        }

        return output;
    }

    public static WhitePickQualityEvaluation EvaluateWhitePick(RgbImageBuffer rgb, WhitePick whitePick)
    {
        var x = Math.Clamp(whitePick.X, 0, rgb.Width - 1);
        var y = Math.Clamp(whitePick.Y, 0, rgb.Height - 1);
        var radius = whitePick.EffectiveRadius;
        var x0 = Math.Max(0, x - radius);
        var x1 = Math.Min(rgb.Width, x + radius + 1);
        var y0 = Math.Max(0, y - radius);
        var y1 = Math.Min(rgb.Height, y + radius + 1);

        var sums = new float[3];
        var luminanceSum = 0.0f;
        var luminanceSquares = 0.0f;
        var count = 0;
        for (var sampleY = y0; sampleY < y1; sampleY++)
        {
            for (var sampleX = x0; sampleX < x1; sampleX++)
            {
                var red = rgb[sampleX, sampleY, 0];
                var green = rgb[sampleX, sampleY, 1];
                var blue = rgb[sampleX, sampleY, 2];
                var luminance = Luminance(red, green, blue);
                sums[0] += red;
                sums[1] += green;
                sums[2] += blue;
                luminanceSum += luminance;
                luminanceSquares += luminance * luminance;
                count++;
            }
        }

        var meanLuminance = luminanceSum / Math.Max(1, count);
        var variance = Math.Max(0.0f, (luminanceSquares / Math.Max(1, count)) - (meanLuminance * meanLuminance));
        var standardDeviation = MathF.Sqrt(variance);
        var means = sums.Select(sum => sum / Math.Max(1, count)).ToArray();
        var channelSpread = means.Max() - means.Min();
        var issue = meanLuminance < 0.10f
            ? WhitePickQualityIssue.TooDark
            : standardDeviation > 0.12f
                ? WhitePickQualityIssue.HighlyTextured
                : channelSpread > 0.15f
                    ? WhitePickQualityIssue.StronglyColored
                    : WhitePickQualityIssue.None;

        return new WhitePickQualityEvaluation(issue, meanLuminance, standardDeviation, channelSpread);
    }

    public static RgbImageBuffer ApplyManualLevelsAndGamma(RgbImageBuffer rgb, float black, float white, float gamma)
    {
        return ApplyLevelCurve(rgb, black, 1.0f / Math.Max(white - black, 1e-6f), gamma);
    }

    private static RgbImageBuffer ApplyNeutralRegionBalance(RgbImageBuffer rgb, float brightnessPercent = 85.0f, float varianceThreshold = 0.08f)
    {
        var brightness = new float[rgb.Width * rgb.Height];
        PixelParallel.For(0, brightness.Length, i =>
        {
            brightness[i] = (rgb.Pixels[i * 3] + rgb.Pixels[(i * 3) + 1] + rgb.Pixels[(i * 3) + 2]) / 3.0f;
        });

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
        PixelParallel.For(0, output.Pixels.Length / 3, pixel =>
        {
            var i = pixel * 3;
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] = Clamp01(output.Pixels[i + c] * target / (means[c] + 1e-6f));
            }
        });

        return output;
    }

    private static RgbImageBuffer ScaleChannelsToWhite(RgbImageBuffer rgb, IReadOnlyList<float> means)
    {
        var output = rgb.Clone();
        PixelParallel.For(0, output.Pixels.Length / 3, pixel =>
        {
            var i = pixel * 3;
            for (var c = 0; c < 3; c++)
            {
                output.Pixels[i + c] *= 1.0f / (means[c] + 1e-6f);
            }
        });

        var max = 0.0f;
        for (var i = 0; i < output.Pixels.Length; i++)
        {
            max = Math.Max(max, output.Pixels[i]);
        }

        max += 1e-6f;
        PixelParallel.For(0, output.Pixels.Length, i =>
        {
            output.Pixels[i] = Clamp01(output.Pixels[i] / max);
        });

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

    private static RgbImageBuffer ApplyLevelCurve(RgbImageBuffer rgb, float black, float gain, float gamma)
    {
        var output = rgb.Clone();
        PixelParallel.For(0, output.Pixels.Length, i =>
        {
            output.Pixels[i] = ApplyLevelCurve(output.Pixels[i], black, gain, gamma);
        });

        return output;
    }

    private static float ApplyLevelCurve(float value, float black, float gain, float gamma)
    {
        var stretched = Clamp01((value - black) * gain);
        return MathF.Pow(stretched, gamma);
    }

    private static float Luminance(float red, float green, float blue) =>
        (0.2126f * red) + (0.7152f * green) + (0.0722f * blue);
}
