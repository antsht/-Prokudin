using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Prokudin.Gui.Services;

public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"https://api.github.com/repos/{DistributionInfo.GitHubOwner}/{DistributionInfo.GitHubRepo}/releases/latest";

        try
        {
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    false,
                    null,
                    null,
                    null,
                    $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            var tagName = root.GetProperty("tag_name").GetString();
            var htmlUrl = root.GetProperty("html_url").GetString();
            var releaseNotes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
            {
                return new UpdateCheckResult(false, null, null, null, "Release metadata is incomplete.");
            }

            if (!TryParseReleaseVersion(tagName, out var latestVersion))
            {
                return new UpdateCheckResult(false, null, null, null, $"Unrecognized release tag: {tagName}.");
            }

            var isUpdateAvailable = latestVersion > NormalizeVersion(currentVersion);
            return new UpdateCheckResult(isUpdateAvailable, latestVersion, htmlUrl, releaseNotes, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, null, null, ex.Message);
        }
    }

    internal static bool TryParseReleaseVersion(string tagName, out Version version)
    {
        var trimmed = tagName.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out version!);
    }

    internal static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build, version.Revision < 0 ? 0 : version.Revision);

    internal static UpdateCheckResult ParseLatestReleaseJson(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString()!;
        var htmlUrl = root.GetProperty("html_url").GetString()!;
        var releaseNotes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;

        if (!TryParseReleaseVersion(tagName, out var latestVersion))
        {
            return new UpdateCheckResult(false, null, null, null, $"Unrecognized release tag: {tagName}.");
        }

        var isUpdateAvailable = latestVersion > NormalizeVersion(currentVersion);
        return new UpdateCheckResult(isUpdateAvailable, latestVersion, htmlUrl, releaseNotes, null);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        var productVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Prokudin", productVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
