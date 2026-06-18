using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public sealed record AlignOptions(
    ChannelName Reference = ChannelName.Green,
    string Detector = "sift",
    int MaxFineIterations = 3,
    bool TrimBorders = true,
    int MaxTranslation = 128)
{
    /// <summary>
    /// Scales with channel size for archival scans. Used when <see cref="MaxTranslation"/> is 0 (auto).
    /// </summary>
    public static int ComputeDefaultMaxTranslation(int width, int height)
    {
        var minDim = Math.Min(width, height);
        return Math.Clamp((int)(minDim * 0.04), 96, 256);
    }

    public int ResolveMaxTranslation(int width, int height)
    {
        return MaxTranslation > 0
            ? MaxTranslation
            : ComputeDefaultMaxTranslation(width, height);
    }
}
