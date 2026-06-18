namespace Prokudin.Core.Imaging;

public static class TriptychSplitter
{
    public static IReadOnlyDictionary<ChannelName, ImageBuffer> SplitTriptych(
        ImageBuffer image,
        TriptychOrder order,
        bool trimBlackBorders = true)
    {
        if (image.Width >= image.Height)
        {
            if (image.Width < 3)
            {
                throw new ArgumentException("Triptych width must be at least 3 pixels.", nameof(image));
            }

            var third = image.Width / 3;
            return MapSegments(
                image.Crop(0, 0, third, image.Height),
                image.Crop(third, 0, third, image.Height),
                image.Crop(third * 2, 0, image.Width - (third * 2), image.Height),
                order,
                trimBlackBorders);
        }

        if (image.Height < 3)
        {
            throw new ArgumentException("Triptych height must be at least 3 pixels.", nameof(image));
        }

        var horizontalThird = image.Height / 3;
        return MapSegments(
            image.Crop(0, 0, image.Width, horizontalThird),
            image.Crop(0, horizontalThird, image.Width, horizontalThird),
            image.Crop(0, horizontalThird * 2, image.Width, image.Height - (horizontalThird * 2)),
            order,
            trimBlackBorders);
    }

    public static TriptychOrder ParseOrder(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "rgb" => TriptychOrder.Rgb,
            "bgr" => TriptychOrder.Bgr,
            _ => throw new ArgumentException("Triptych order must be 'rgb' or 'bgr'.", nameof(value)),
        };
    }

    private static IReadOnlyDictionary<ChannelName, ImageBuffer> MapSegments(
        ImageBuffer first,
        ImageBuffer second,
        ImageBuffer third,
        TriptychOrder order,
        bool trimBlackBorders)
    {
        var segments = new[] { first, second, third };
        if (trimBlackBorders)
        {
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = ImageLoader.TrimBlackBorders(segments[i]);
            }
        }

        return order switch
        {
            TriptychOrder.Rgb => new Dictionary<ChannelName, ImageBuffer>
            {
                [ChannelName.Red] = segments[0],
                [ChannelName.Green] = segments[1],
                [ChannelName.Blue] = segments[2],
            },
            TriptychOrder.Bgr => new Dictionary<ChannelName, ImageBuffer>
            {
                [ChannelName.Red] = segments[2],
                [ChannelName.Green] = segments[1],
                [ChannelName.Blue] = segments[0],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
        };
    }
}
