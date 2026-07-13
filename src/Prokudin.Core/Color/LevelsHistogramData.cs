using Prokudin.Core.Imaging;

namespace Prokudin.Core.Color;

/// <summary>
/// Normalized input-distribution data for the master and channel level scopes.
/// </summary>
public sealed record LevelsHistogramData(
    IReadOnlyList<double> Master,
    IReadOnlyList<double> Red,
    IReadOnlyList<double> Green,
    IReadOnlyList<double> Blue)
{
    public IReadOnlyList<double> ForScope(LevelsScope scope) => scope switch
    {
        LevelsScope.Master => Master,
        LevelsScope.Red => Red,
        LevelsScope.Green => Green,
        LevelsScope.Blue => Blue,
        _ => Master,
    };
}

public static class LevelsHistogramCalculator
{
    public const int DefaultBinCount = 128;

    /// <summary>
    /// Builds channel distributions from the image before channel levels and a luminance
    /// distribution from the image before master levels.
    /// </summary>
    public static LevelsHistogramData Calculate(
        RgbImageBuffer beforeChannelLevels,
        RgbImageBuffer beforeMasterLevels,
        int binCount = DefaultBinCount)
    {
        ArgumentNullException.ThrowIfNull(beforeChannelLevels);
        ArgumentNullException.ThrowIfNull(beforeMasterLevels);
        ArgumentOutOfRangeException.ThrowIfLessThan(binCount, 2);

        var red = new int[binCount];
        var green = new int[binCount];
        var blue = new int[binCount];
        var master = new int[binCount];

        AddChannelBins(beforeChannelLevels.Pixels, red, green, blue, binCount);
        AddLuminanceBins(beforeMasterLevels.Pixels, master, binCount);

        return new LevelsHistogramData(
            Normalize(master),
            Normalize(red),
            Normalize(green),
            Normalize(blue));
    }

    private static void AddChannelBins(
        IReadOnlyList<float> pixels,
        int[] red,
        int[] green,
        int[] blue,
        int binCount)
    {
        for (var i = 0; i < pixels.Count; i += 3)
        {
            red[ToBin(pixels[i], binCount)]++;
            green[ToBin(pixels[i + 1], binCount)]++;
            blue[ToBin(pixels[i + 2], binCount)]++;
        }
    }

    private static void AddLuminanceBins(IReadOnlyList<float> pixels, int[] master, int binCount)
    {
        for (var i = 0; i < pixels.Count; i += 3)
        {
            var luminance = (0.2126f * pixels[i]) + (0.7152f * pixels[i + 1]) + (0.0722f * pixels[i + 2]);
            master[ToBin(luminance, binCount)]++;
        }
    }

    private static int ToBin(float value, int binCount) =>
        Math.Min((int)(Math.Clamp(value, 0.0f, 1.0f) * binCount), binCount - 1);

    private static double[] Normalize(IReadOnlyList<int> counts)
    {
        var maximum = counts.Max();
        if (maximum == 0)
        {
            return new double[counts.Count];
        }

        var normalized = new double[counts.Count];
        for (var i = 0; i < counts.Count; i++)
        {
            normalized[i] = (double)counts[i] / maximum;
        }

        return normalized;
    }
}
