using Prokudin.Core.Imaging;

namespace Prokudin.Core.Color;

public sealed record ChannelExposureSettings(
    float RedStops = 0.0f,
    float GreenStops = 0.0f,
    float BlueStops = 0.0f)
{
    public float For(ChannelName channelName)
    {
        return channelName switch
        {
            ChannelName.Red => RedStops,
            ChannelName.Green => GreenStops,
            ChannelName.Blue => BlueStops,
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };
    }
}
