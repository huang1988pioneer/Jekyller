using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public static partial class JekyllVersionComparison
{
    public static JekyllVersionCheckResult Create(
        string installedOutput,
        string remoteOutput,
        bool remoteCheckSucceeded)
    {
        var installed = ExtractVersion(installedOutput);
        if (string.IsNullOrWhiteSpace(installed))
        {
            return new JekyllVersionCheckResult
            {
                Message = "尚未安裝 Jekyll。安裝後即可檢查最新版本。"
            };
        }

        var latest = ExtractRemoteVersion(remoteOutput);
        if (!remoteCheckSucceeded || string.IsNullOrWhiteSpace(latest))
        {
            return new JekyllVersionCheckResult
            {
                IsInstalled = true,
                InstalledVersion = installed,
                Message = $"目前 Jekyll {installed}；暫時無法連線至 RubyGems 檢查最新版本。"
            };
        }

        var updateAvailable = Compare(installed, latest) < 0;
        return new JekyllVersionCheckResult
        {
            IsInstalled = true,
            CheckSucceeded = true,
            IsUpdateAvailable = updateAvailable,
            InstalledVersion = installed,
            LatestVersion = latest,
            Message = updateAvailable
                ? $"發現新版 Jekyll：目前 {installed}，最新 {latest}。建議更新後再建立或建置網站。"
                : $"Jekyll 已是最新版本（{installed}）。"
        };
    }

    private static string ExtractVersion(string value)
    {
        var match = SemanticVersionRegex().Match(value ?? string.Empty);
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }

    private static string ExtractRemoteVersion(string value)
    {
        var match = RemoteJekyllRegex().Match(value ?? string.Empty);
        if (match.Success)
            return match.Groups["version"].Value;

        match = RubyGemsJsonVersionRegex().Match(value ?? string.Empty);
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }

    private static int Compare(string left, string right)
    {
        return Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion)
            ? leftVersion.CompareTo(rightVersion)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(?<version>\d+\.\d+(?:\.\d+){0,2})")]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex(@"jekyll\s*\(\s*(?<version>\d+\.\d+(?:\.\d+){0,2})", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteJekyllRegex();

    [GeneratedRegex("\\\"version\\\"\\s*:\\s*\\\"(?<version>\\d+\\.\\d+(?:\\.\\d+){0,2})\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex RubyGemsJsonVersionRegex();
}
