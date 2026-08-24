using System.Text.RegularExpressions;

namespace Jekyller.Services;

internal static class JekyllLocalPreview
{
    public static string BuildServeArguments(int port, bool liveReload, bool windows)
    {
        var args = $"serve --host 127.0.0.1 --port {port} --future --drafts";
        if (windows)
            args += " --force_polling";
        if (liveReload)
            args += " --livereload";
        return args;
    }

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim().Trim('"', '\'');
        if (value.Length == 0 || value == "/")
            return string.Empty;
        if (!value.StartsWith('/'))
            value = "/" + value;
        return value.TrimEnd('/');
    }

    public static string BuildSiteUrl(int port, string? baseUrl)
    {
        var path = NormalizeBaseUrl(baseUrl);
        return string.IsNullOrEmpty(path)
            ? $"http://127.0.0.1:{port}/"
            : $"http://127.0.0.1:{port}{path}/";
    }

    public static string EnsureSiteUrl(string candidate, int port, string? baseUrl)
    {
        var path = NormalizeBaseUrl(baseUrl);
        var url = (candidate ?? string.Empty).Trim();
        if (url.Length == 0)
            return BuildSiteUrl(port, path);

        if (!string.IsNullOrEmpty(path)
            && !url.Contains(path, StringComparison.OrdinalIgnoreCase))
            return BuildSiteUrl(port, path);

        if (!url.EndsWith('/'))
            url += "/";
        return url;
    }

    public static string ParseBaseUrlFromYaml(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return string.Empty;

        foreach (var rawLine in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (!line.StartsWith("baseurl", StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf(':');
            if (separator < 0)
                continue;

            var value = line[(separator + 1)..].Trim();
            var comment = value.IndexOf('#');
            if (comment >= 0)
                value = value[..comment].Trim();
            return NormalizeBaseUrl(value);
        }

        return string.Empty;
    }

    public static string FallbackPostTitle(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "未命名";

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(stem, @"^\d{4}-\d{2}-\d{2}-(.+)$");
        var slug = match.Success ? match.Groups[1].Value : stem;
        slug = slug.Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(slug) ? "未命名" : slug;
    }
}
