using Prokudin.Core.Alignment;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Pipeline;

public static class AlignedChannelCropper
{
    public static (AlignedChannels Channels, CropInfo CropInfo) CropToLargestFullOverlap(AlignedChannels aligned)
    {
        EnsureSameDimensions(aligned);
        var crop = Cropper.LargestFullOverlapRectangle(
            aligned.MaskRed,
            aligned.MaskGreen,
            aligned.MaskBlue,
            aligned.Red.Width,
            aligned.Red.Height) ?? throw new InvalidOperationException("No full overlap between aligned channels; cannot crop.");

        var cropInfo = new CropInfo(crop.X0, crop.Y0, crop.X1, crop.Y1, crop.X0, crop.Y0, crop.X1, crop.Y1);
        return (Crop(aligned, cropInfo), cropInfo);
    }

    public static AlignedChannels Crop(AlignedChannels aligned, CropInfo crop)
    {
        EnsureSameDimensions(aligned);
        var width = crop.X1 - crop.X0;
        var height = crop.Y1 - crop.Y0;
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop rectangle must have positive dimensions.");
        }

        return new AlignedChannels(
            aligned.Red.Crop(crop.X0, crop.Y0, width, height),
            aligned.Green.Crop(crop.X0, crop.Y0, width, height),
            aligned.Blue.Crop(crop.X0, crop.Y0, width, height),
            Cropper.CropMask(aligned.MaskRed, aligned.Red.Width, aligned.Red.Height, crop),
            Cropper.CropMask(aligned.MaskGreen, aligned.Green.Width, aligned.Green.Height, crop),
            Cropper.CropMask(aligned.MaskBlue, aligned.Blue.Width, aligned.Blue.Height, crop),
            aligned.AlignMetadata,
            IdentityTransforms(width, height));
    }

    public static AlignedChannels FromPreparedChannels(
        ImageBuffer red,
        ImageBuffer green,
        ImageBuffer blue,
        IReadOnlyDictionary<ChannelName, AlignChannelMetadata> metadata)
    {
        if (red.Width != green.Width || red.Width != blue.Width || red.Height != green.Height || red.Height != blue.Height)
        {
            throw new ArgumentException("All prepared channels must have the same dimensions.");
        }

        var mask = Ones(red.Width * red.Height);
        return new AlignedChannels(
            red,
            green,
            blue,
            (byte[])mask.Clone(),
            (byte[])mask.Clone(),
            (byte[])mask.Clone(),
            metadata,
            IdentityTransforms(red.Width, red.Height));
    }

    private static void EnsureSameDimensions(AlignedChannels aligned)
    {
        if (aligned.Red.Width != aligned.Green.Width ||
            aligned.Red.Width != aligned.Blue.Width ||
            aligned.Red.Height != aligned.Green.Height ||
            aligned.Red.Height != aligned.Blue.Height)
        {
            throw new ArgumentException("All aligned channels must have the same dimensions.", nameof(aligned));
        }
    }

    private static IReadOnlyDictionary<ChannelName, ChannelAlignmentTransform> IdentityTransforms(int width, int height)
    {
        return new Dictionary<ChannelName, ChannelAlignmentTransform>
        {
            [ChannelName.Red] = ChannelAlignmentTransform.Identity(width, height, "prepared"),
            [ChannelName.Green] = ChannelAlignmentTransform.Identity(width, height, "prepared"),
            [ChannelName.Blue] = ChannelAlignmentTransform.Identity(width, height, "prepared"),
        };
    }

    private static byte[] Ones(int length)
    {
        var values = new byte[length];
        Array.Fill(values, (byte)1);
        return values;
    }
}
