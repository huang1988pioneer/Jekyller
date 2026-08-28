using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jekyller.Models;

namespace Jekyller.Services;

public sealed class DeploymentMonitorService
{
    public const string MarkerFileName = DeploymentMarkerFiles.PrimaryFileName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HttpClient Client = CreateHttpClient();

    public async Task<DeploymentMarker> PrepareDeploymentAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var marker = new DeploymentMarker
        {
            DeploymentId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(marker, JsonOptions);
        foreach (var markerPath in DeploymentMarkerFiles.SourceMarkerPaths(sitePath))
        {
            await File.WriteAllTextAsync(
                markerPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
        }
        return marker;
    }

    public async Task<DeploymentCheckResult> CheckAsync(
        string sitePath,
        string? pagesUrl,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.Now;
        var expected = await ReadExpectedMarkerAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (expected is null)
        {
            return new DeploymentCheckResult
            {
                State = DeploymentVersionState.NotConfigured,
                Message = "尚未建立部署版本標記；下次推送後會開始辨識線上版本。",
                CheckedAt = checkedAt
            };
        }

        if (!TryBuildMarkerUri(pagesUrl, MarkerFileName, expected.DeploymentId, out _))
        {
            return new DeploymentCheckResult
            {
                State = DeploymentVersionState.NotConfigured,
                Message = "尚未取得有效的 Pages 網址，無法檢查線上版本。",
                ExpectedDeploymentId = expected.DeploymentId,
                CheckedAt = checkedAt
            };
        }

        try
        {
            foreach (var fileName in DeploymentMarkerFiles.ReadCandidates)
            {
                TryBuildMarkerUri(pagesUrl, fileName, expected.DeploymentId, out var markerUri);
                using var request = new HttpRequestMessage(HttpMethod.Get, markerUri);
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };
                using var response = await Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    continue;

                if (PagesAccessStatus.TryCreateProtectedSiteMessage(
                        response.StatusCode,
                        response.Headers.Location,
                        out var protectedSiteMessage))
                {
                    return Unavailable(expected, checkedAt, protectedSiteMessage);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Unavailable(expected, checkedAt,
                        $"暫時無法檢查線上版本（HTTP {(int)response.StatusCode}）。5 分鐘後會自動重試。");
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                DeploymentMarker? live;
                try
                {
                    live = JsonSerializer.Deserialize<DeploymentMarker>(json, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (live is null || string.IsNullOrWhiteSpace(live.DeploymentId))
                    continue;

                return string.Equals(live.DeploymentId, expected.DeploymentId, StringComparison.Ordinal)
                    ? new DeploymentCheckResult
                    {
                        State = DeploymentVersionState.Latest,
                        Message = "線上網站已是最新版本。",
                        ExpectedDeploymentId = expected.DeploymentId,
                        LiveDeploymentId = live.DeploymentId,
                        CheckedAt = checkedAt
                    }
                    : Previous(expected, live.DeploymentId, checkedAt,
                        PagesAccessStatus.WithGitLabCacheHint(
                            pagesUrl,
                            "線上網站仍是上一版本；Pages 尚在部署最新內容。"));
            }

            return Previous(expected, null, checkedAt,
                PagesAccessStatus.WithGitLabCacheHint(
                    pagesUrl,
                    "線上網站仍是上一版本；尚未找到最新部署標記。"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(expected, checkedAt, "檢查線上版本逾時；5 分鐘後會自動重試。");
        }
        catch (HttpRequestException ex)
        {
            return Unavailable(expected, checkedAt,
                $"目前無法連線到網站：{ex.Message}。5 分鐘後會自動重試。");
        }
    }

    private static async Task<DeploymentMarker?> ReadExpectedMarkerAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        foreach (var markerPath in DeploymentMarkerFiles.ExpectedMarkerPaths(sitePath))
        {
            if (!File.Exists(markerPath)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
                var marker = JsonSerializer.Deserialize<DeploymentMarker>(json, JsonOptions);
                if (marker is not null && !string.IsNullOrWhiteSpace(marker.DeploymentId))
                    return marker;
            }
            catch (JsonException)
            {
                // Try the other supported marker name.
            }
        }

        return null;
    }

    private static bool TryBuildMarkerUri(string? pagesUrl, string fileName, string expectedId, out Uri markerUri)
    {
        markerUri = null!;
        if (!Uri.TryCreate(pagesUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var root = new Uri(baseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        markerUri = new Uri(root,
            $"{fileName}?deployment={Uri.EscapeDataString(expectedId)}&checked={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        return true;
    }

    private static DeploymentCheckResult Previous(
        DeploymentMarker expected,
        string? liveId,
        DateTimeOffset checkedAt,
        string message) => new()
        {
            State = DeploymentVersionState.Previous,
            Message = message,
            ExpectedDeploymentId = expected.DeploymentId,
            LiveDeploymentId = liveId,
            CheckedAt = checkedAt
        };

    private static DeploymentCheckResult Unavailable(
        DeploymentMarker expected,
        DateTimeOffset checkedAt,
        string message) => new()
        {
            State = DeploymentVersionState.Unavailable,
            Message = message,
            ExpectedDeploymentId = expected.DeploymentId,
            CheckedAt = checkedAt
        };

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jekyller-Deployment-Monitor/1.0");
        return client;
    }
}
