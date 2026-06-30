using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.Editing;

public enum EditorMementoKind
{
    Parameter,
    Snapshot,
}

public sealed record EditorMemento(
    EditorMementoKind Kind,
    ImageBuffer? Red,
    ImageBuffer? Green,
    ImageBuffer? Blue,
    string? RedSourcePath,
    string? GreenSourcePath,
    string? BlueSourcePath,
    AlignedChannels? LastAligned,
    double RedExposureStops,
    double GreenExposureStops,
    double BlueExposureStops,
    bool AutoWhiteBalance,
    int WhiteBalancePipetteX,
    int WhiteBalancePipetteY,
    LevelsMode LevelsMode,
    double LevelsBlackPoint,
    double LevelsWhitePoint,
    double LevelsGamma,
    int ColorTemperature,
    int ColorTint,
    string? SelectedSlotDisplayName)
{
    public long ApproximateBytes =>
        EstimateImageBytes(Red) +
        EstimateImageBytes(Green) +
        EstimateImageBytes(Blue) +
        EstimateAlignedBytes(LastAligned);

    private static long EstimateAlignedBytes(AlignedChannels? aligned)
    {
        if (aligned is null)
        {
            return 0;
        }

        return EstimateImageBytes(aligned.Red) +
            EstimateImageBytes(aligned.Green) +
            EstimateImageBytes(aligned.Blue) +
            aligned.MaskRed.LongLength +
            aligned.MaskGreen.LongLength +
            aligned.MaskBlue.LongLength;
    }

    private static long EstimateImageBytes(ImageBuffer? image)
    {
        if (image is null)
        {
            return 0;
        }

        return image.PixelCount * BytesPerPixel(image.Format);
    }

    private static int BytesPerPixel(PixelFormat format) =>
        format switch
        {
            PixelFormat.UInt8 => sizeof(byte),
            PixelFormat.UInt16 => sizeof(ushort),
            PixelFormat.Float32 => sizeof(float),
            _ => sizeof(float),
        };
}
