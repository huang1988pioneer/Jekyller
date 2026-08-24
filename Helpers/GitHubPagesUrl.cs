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
            if (!value.Contains(".github.io", StringComparison.OrdinalIgnoreCase))
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

        const string suffix = ".github.io";
        var host = uri.IdnHost;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || host.Length <= suffix.Length)
            return null;

        var owner = host[..^suffix.Length];
        if (string.IsNullOrWhiteSpace(owner) || owner.Contains('.', StringComparison.Ordinal))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var repository = segments.Length == 0 ? $"{owner}.github.io" : segments[0];
        return $"https://github.com/{owner}/{repository}";
    }
}
