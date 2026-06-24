using FluentAssertions;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class GitHubReleaseUpdateCheckerTests
{
    [Theory]
    [InlineData("v0.9.0", 0, 8, 0, true)]
    [InlineData("0.9.0", 0, 8, 0, true)]
    [InlineData("v0.8.0", 0, 8, 0, false)]
    [InlineData("v0.8.1", 0, 8, 0, true)]
    public void ParseLatestReleaseJson_ComparesVersions(
        string tagName,
        int currentMajor,
        int currentMinor,
        int currentBuild,
        bool expectsUpdate)
    {
        var json = CreateReleaseJson(tagName);
        var current = new Version(currentMajor, currentMinor, currentBuild);

        var result = GitHubReleaseUpdateChecker.ParseLatestReleaseJson(json, current);

        result.ErrorMessage.Should().BeNull();
        result.IsUpdateAvailable.Should().Be(expectsUpdate);
        result.ReleaseUrl.Should().Be($"https://github.com/antsht/-Prokudin/releases/tag/{tagName}");
    }

    [Theory]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    public void TryParseReleaseVersion_ParsesTag(string tagName, int major, int minor, int build)
    {
        GitHubReleaseUpdateChecker.TryParseReleaseVersion(tagName, out var version).Should().BeTrue();
        version.Should().Be(new Version(major, minor, build));
    }

    [Fact]
    public void NormalizeVersion_TreatsMissingBuildAsZero()
    {
        GitHubReleaseUpdateChecker.NormalizeVersion(new Version(0, 8)).Should().Be(new Version(0, 8, 0, 0));
    }

    private static string CreateReleaseJson(string tagName) =>
        $$"""
        {
          "tag_name": "{{tagName}}",
          "html_url": "https://github.com/antsht/-Prokudin/releases/tag/{{tagName}}",
          "body": "Distribution release."
        }
        """;
}
