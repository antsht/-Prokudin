using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Pipeline;

public sealed record AlignedChannels(
    ImageBuffer Red,
    ImageBuffer Green,
    ImageBuffer Blue,
    byte[] MaskRed,
    byte[] MaskGreen,
    byte[] MaskBlue,
    IReadOnlyDictionary<ChannelName, AlignChannelMetadata> AlignMetadata);
