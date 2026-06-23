namespace Prokudin.Core.Retouch;

public static class AutoCleanQualityProfiles
{
    public static (AutoCleanSettings Detect, HealOptions Apply) Resolve(
        AutoCleanQualityMode mode,
        AutoCleanSettings userDetect,
        HealOptions userApply)
    {
        var detect = mode switch
        {
            AutoCleanQualityMode.Balanced => userDetect with { AutoMergeDistancePx = 5 },
            AutoCleanQualityMode.Fast => userDetect with { AutoMergeDistancePx = 8 },
            _ => userDetect,
        };

        var apply = mode switch
        {
            AutoCleanQualityMode.Balanced => userApply with
            {
                QualityMode = mode,
                SearchRadius = 32,
                UseGuidedPatchSearch = true,
                UseLocalLinearPrediction = true,
                LowConfidenceThreshold = 0.25f,
                AllowSoftFastPath = true,
            },
            AutoCleanQualityMode.Fast => userApply with
            {
                QualityMode = mode,
                UseGuidedPatchSearch = false,
                UseLocalLinearPrediction = false,
                LowConfidenceThreshold = 0.15f,
                AllowSoftFastPath = true,
            },
            _ => userApply with
            {
                QualityMode = mode,
                SearchRadius = 48,
                UseGuidedPatchSearch = true,
                UseLocalLinearPrediction = true,
                LowConfidenceThreshold = 0.35f,
                AllowSoftFastPath = true,
            },
        };

        return (detect, apply);
    }
}
