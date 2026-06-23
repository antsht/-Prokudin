namespace Prokudin.Core.Retouch;

using Prokudin.Core.Diagnostics;

public sealed record AutoCleanSettings(
    int Sensitivity = 50,
    int InpaintRadius = 3,
    int AutoExpandHealingAreaPx = 2,
    bool AutoMergeNearbyDefects = true,
    int AutoMergeDistancePx = 3,
    int MaxAutoExpandedComponentArea = 10000,
    bool DebugOutput = false,
    string? DebugOutputDirectory = null,
    string? DebugMaskPrefix = null,
    IProcessingDiagnostics? Diagnostics = null)
{
    public int NormalizedSensitivity => Math.Clamp(Sensitivity, 0, 100);

    public int NormalizedInpaintRadius => Math.Clamp(InpaintRadius, 1, 24);

    public int NormalizedAutoExpandHealingAreaPx => Math.Clamp(AutoExpandHealingAreaPx, 0, 10);

    public int NormalizedAutoMergeDistancePx => Math.Clamp(AutoMergeDistancePx, 0, 20);

    public int NormalizedMaxAutoExpandedComponentArea => Math.Max(1, MaxAutoExpandedComponentArea);
}
