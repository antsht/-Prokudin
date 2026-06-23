namespace Prokudin.Core.Retouch;

public sealed record AutoCleanMaskResult(byte[] Mask, int CandidatePixels)
{
    public byte[] RawMask { get; init; } = Mask;

    public byte[] MergedMask { get; init; } = Mask;

    public byte[] ExpandedMask { get; init; } = Mask;

    public byte[] FinalMask { get; init; } = Mask;
}
