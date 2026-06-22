namespace Prokudin.Core.Retouch;

public sealed record HealOptions(
    HealingMode Mode = HealingMode.CrossChannelGuided,
    HealingSubMode SubMode = HealingSubMode.Patch,
    int PatchRadius = 3,
    int SearchRadius = 48,
    int SafetyRadius = 2,
    int ContextRadius = 16,
    int MinTrainingPixels = 64,
    bool UseLocalLinearPrediction = true,
    bool UseGuidedPatchSearch = true,
    bool UseRobustFit = true,
    float PredictionAlphaMin = 0.15f,
    float PredictionAlphaMax = 0.75f,
    float WGuide = 0.45f,
    float WGradient = 0.25f,
    float WBoundary = 0.25f,
    float WDistance = 0.05f,
    float FeatherSigma = 1.5f,
    float MaxAllowedErrorFloat = 0.12f,
    float MaxAllowedErrorUint8 = 30.0f / 255.0f,
    float MaxAllowedErrorUint16 = 7700.0f / 65535.0f,
    int MaxComponentArea = 5000,
    float LowConfidenceThreshold = 0.35f,
    float LargeComponentConservativeScale = 0.5f,
    bool DebugOutput = false,
    string? DebugOutputDirectory = null)
{
    public int NormalizedPatchRadius => Math.Clamp(PatchRadius, 1, 12);

    public int NormalizedSearchRadius => Math.Clamp(SearchRadius, 8, 192);

    public int NormalizedSafetyRadius => Math.Clamp(SafetyRadius, 0, 12);

    public int NormalizedContextRadius => Math.Clamp(ContextRadius, 4, 64);

    public int NormalizedInpaintRadius => Math.Clamp(PatchRadius + 1, 1, 24);
}
