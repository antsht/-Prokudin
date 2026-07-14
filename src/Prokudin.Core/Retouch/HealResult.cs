using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

public sealed record HealResult(
    ImageBuffer Image,
    byte[] Mask,
    float AverageConfidence = 0.0f,
    bool UsedCrossChannel = false,
    bool UsedFallback = false,
    string? StatusMessage = null,
    RetouchProvenanceMap? Provenance = null,
    GuidedHealingSummary? GuidedSummary = null)
{
    public bool IsLowConfidence => GuidedSummary?.HasLowConfidence ?? false;
}
