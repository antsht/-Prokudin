using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public sealed record ChannelAlignmentTransform(
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    string TransformKind,
    int MatrixRows,
    int MatrixColumns,
    double[] Matrix,
    IReadOnlyList<(float Dx, float Dy)> Shifts)
{
    public static ChannelAlignmentTransform Identity(int width, int height, string kind = "identity")
    {
        return new ChannelAlignmentTransform(
            width,
            height,
            width,
            height,
            kind,
            3,
            3,
            [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0],
            []);
    }

    public bool CanApplyTo(ImageBuffer image)
    {
        return image.Width == SourceWidth && image.Height == SourceHeight;
    }
}
