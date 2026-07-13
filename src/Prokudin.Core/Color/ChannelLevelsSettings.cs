namespace Prokudin.Core.Color;

public sealed record ChannelLevelsSettings(
    ChannelLevelSettings? Red = null,
    ChannelLevelSettings? Green = null,
    ChannelLevelSettings? Blue = null)
{
    public ChannelLevelSettings ForIndex(int channelIndex) =>
        channelIndex switch
        {
            0 => Red ?? new ChannelLevelSettings(),
            1 => Green ?? new ChannelLevelSettings(),
            2 => Blue ?? new ChannelLevelSettings(),
            _ => throw new ArgumentOutOfRangeException(nameof(channelIndex), channelIndex, null),
        };

    public bool IsNeutral => ForIndex(0).IsNeutral && ForIndex(1).IsNeutral && ForIndex(2).IsNeutral;
}
