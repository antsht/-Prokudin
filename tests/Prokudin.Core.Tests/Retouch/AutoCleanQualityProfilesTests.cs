using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class AutoCleanQualityProfilesTests
{
    [Theory]
    [InlineData(AutoCleanQualityMode.Quality, 48, true, true, 3)]
    [InlineData(AutoCleanQualityMode.Balanced, 32, true, true, 5)]
    [InlineData(AutoCleanQualityMode.Fast, 48, false, false, 8)]
    public void Resolve_AppliesExpectedHealOverrides(
        AutoCleanQualityMode mode,
        int searchRadius,
        bool guidedPatch,
        bool localPrediction,
        int mergeDistance)
    {
        var userDetect = new AutoCleanSettings(Sensitivity: 50, AutoMergeDistancePx: 3);
        var userApply = new HealOptions(PatchRadius: 4);

        var (detect, apply) = AutoCleanQualityProfiles.Resolve(mode, userDetect, userApply);

        detect.AutoMergeDistancePx.Should().Be(mergeDistance);
        apply.SearchRadius.Should().Be(searchRadius);
        apply.UseGuidedPatchSearch.Should().Be(guidedPatch);
        apply.UseLocalLinearPrediction.Should().Be(localPrediction);
        apply.QualityMode.Should().Be(mode);
        apply.PatchRadius.Should().Be(4);
    }
}
