using Prokudin.Core.Retouch;

namespace Prokudin.Gui.Services;

public sealed record AutoCleanSettingsSnapshot(
    AutoCleanQualityMode QualityMode = AutoCleanQualityMode.Quality,
    int Sensitivity = 50,
    int InpaintRadius = 3,
    int PatchRadius = 3,
    int SearchRadius = 48,
    int SafetyRadius = 2,
    int ContextRadius = 16,
    int MinTrainingPixels = 64,
    bool UseCrossChannelHealing = true,
    bool UseTeleaHealing = false,
    bool UseLocalLinearPrediction = true,
    bool UseGuidedPatchSearch = true,
    bool UseRobustFit = true,
    bool AutoMergeNearbyDefects = true,
    int AutoMergeDistancePx = 3,
    int AutoExpandHealingAreaPx = 2,
    int MaxComponentArea = 5000,
    float PredictionAlphaMin = 0.15f,
    float PredictionAlphaMax = 0.75f,
    float FeatherSigma = 1.5f,
    float MaxAllowedError = 0.12f,
    float LargeComponentConservativeScale = 0.5f,
    bool DebugHealOutput = false,
    bool ShowHealMaskOverlay = true);
