namespace Prokudin.Core.Retouch;

public sealed record GuidedHealingSummary(
    int CompactComponents,
    int ScratchComponents,
    int LowConfidenceComponents,
    int ExcludedGuides,
    int BoundarySegments)
{
    public bool HasLowConfidence => LowConfidenceComponents > 0;
}
