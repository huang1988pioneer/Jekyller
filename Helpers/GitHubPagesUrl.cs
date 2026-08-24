namespace Jekyller.Helpers;

public static class GitHubPagesUrl
{
    public static string? TryConvertToRepositoryUrl(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            if (!IsSupportedPagesHost(value))
                return null;
            value = $"https://{value.TrimStart('/')}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? TryConvertToRepositoryUrl(uri)
            : null;
    }

    public static string? TryConvertToRepositoryUrl(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return null;

        var host = uri.IdnHost;
        var mapping = PlatformMappings.FirstOrDefault(item =>
            host.EndsWith(item.PagesHostSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > item.PagesHostSuffix.Length);
        if (mapping.PagesHostSuffix is null)
            return null;

        var owner = host[..^mapping.PagesHostSuffix.Length];
        if (string.IsNullOrWhiteSpace(owner) || owner.Contains('.', StringComparison.Ordinal))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var repository = segments.Length == 0 ? mapping.UserSiteRepository(owner) : segments[0];
        return $"https://{mapping.RepositoryHost}/{owner}/{repository}";
    }

    private static bool IsSupportedPagesHost(string value) =>
        PlatformMappings.Any(item => value.Contains(item.PagesHostSuffix, StringComparison.OrdinalIgnoreCase));

    private static readonly PlatformPagesMapping[] PlatformMappings =
    [
        new(".github.io", "github.com", owner => $"{owner}.github.io"),
        new(".gitlab.io", "gitlab.com", owner => $"{owner}.gitlab.io"),
        new(".codeberg.page", "codeberg.org", _ => "pages"),
        new(".bitbucket.io", "bitbucket.org", owner => $"{owner}.bitbucket.io")
    ];

    private readonly record struct PlatformPagesMapping(
        string PagesHostSuffix,
        string RepositoryHost,
        Func<string, string> UserSiteRepository);
}
