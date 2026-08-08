using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Jekyller.Services;

public sealed class JekyllConfigSnapshot
{
    public string RawYaml { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public string RemoteTheme { get; init; } = string.Empty;
    public string Lang { get; init; } = string.Empty;
    public string Timezone { get; init; } = string.Empty;
    public string Markdown { get; init; } = string.Empty;
    public string Permalink { get; init; } = string.Empty;
}

public interface IConfigService
{
    string? FindConfigPath(string projectPath);
    Task<JekyllConfigSnapshot> LoadAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveRawAsync(string projectPath, string rawYaml, CancellationToken cancellationToken = default);
    Task SaveFieldsAsync(string projectPath, JekyllConfigSnapshot fields, CancellationToken cancellationToken = default);
    Task<string?> LoadThemeConfigAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveThemeConfigAsync(string projectPath, string rawYaml, CancellationToken cancellationToken = default);
    string? FindThemeConfigPath(string projectPath);
}

public sealed class ConfigService : IConfigService
{
    public string? FindConfigPath(string projectPath)
    {
        var yml = Path.Combine(projectPath, "_config.yml");
        if (File.Exists(yml)) return yml;
        var yaml = Path.Combine(projectPath, "_config.yaml");
        return File.Exists(yaml) ? yaml : yml;
    }

    public string? FindThemeConfigPath(string projectPath)
    {
        // Chirpy and some themes use _config.yml only; others may have _data or theme config.
        var candidates = new[]
        {
            Path.Combine(projectPath, "_config.yml"),
            Path.Combine(projectPath, "docs", "_config.yml"),
            Path.Combine(projectPath, "_data", "theme.yml"),
            Path.Combine(projectPath, "_sass", "theme-config.yml")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<JekyllConfigSnapshot> LoadAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var path = FindConfigPath(projectPath);
        if (path is null || !File.Exists(path))
        {
            return new JekyllConfigSnapshot
            {
                RawYaml = """
                          title: My Jekyll Site
                          description: A site created with Jekyller
                          url: ""
                          baseurl: ""
                          """
            };
        }

        var raw = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Parse(raw);
    }

    public async Task SaveRawAsync(string projectPath, string rawYaml, CancellationToken cancellationToken = default)
    {
        var path = FindConfigPath(projectPath) ?? Path.Combine(projectPath, "_config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, NormalizeNewlines(rawYaml), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveFieldsAsync(string projectPath, JekyllConfigSnapshot fields, CancellationToken cancellationToken = default)
    {
        var path = FindConfigPath(projectPath) ?? Path.Combine(projectPath, "_config.yml");
        var raw = File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false)
            : fields.RawYaml;

        raw = UpsertScalar(raw, "title", fields.Title);
        raw = UpsertScalar(raw, "description", fields.Description);
        raw = UpsertScalar(raw, "url", fields.Url);
        raw = UpsertScalar(raw, "baseurl", fields.BaseUrl);
        raw = UpsertScalar(raw, "email", fields.Email);
        raw = UpsertScalar(raw, "theme", fields.Theme);
        raw = UpsertScalar(raw, "remote_theme", fields.RemoteTheme);
        raw = UpsertScalar(raw, "lang", fields.Lang);
        raw = UpsertScalar(raw, "timezone", fields.Timezone);
        raw = UpsertScalar(raw, "markdown", fields.Markdown);
        raw = UpsertScalar(raw, "permalink", fields.Permalink);

        await File.WriteAllTextAsync(path, NormalizeNewlines(raw), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> LoadThemeConfigAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var path = FindThemeConfigPath(projectPath);
        if (path is null || !File.Exists(path))
            return null;
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveThemeConfigAsync(string projectPath, string rawYaml, CancellationToken cancellationToken = default)
    {
        var path = FindThemeConfigPath(projectPath) ?? Path.Combine(projectPath, "_config.yml");
        await File.WriteAllTextAsync(path, NormalizeNewlines(rawYaml), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static JekyllConfigSnapshot Parse(string raw)
    {
        var snapshot = new JekyllConfigSnapshot { RawYaml = raw };
        try
        {
            using var reader = new StringReader(raw);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode map)
                return snapshot;

            string Get(string key)
            {
                foreach (var entry in map.Children)
                {
                    if (entry.Key is YamlScalarNode { Value: { } k } &&
                        string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value switch
                        {
                            YamlScalarNode s => s.Value ?? string.Empty,
                            _ => entry.Value.ToString() ?? string.Empty
                        };
                    }
                }

                return string.Empty;
            }

            return new JekyllConfigSnapshot
            {
                RawYaml = raw,
                Title = Get("title"),
                Description = Get("description"),
                Url = Get("url"),
                BaseUrl = Get("baseurl"),
                Email = Get("email"),
                Theme = Get("theme"),
                RemoteTheme = Get("remote_theme"),
                Lang = Get("lang"),
                Timezone = Get("timezone"),
                Markdown = Get("markdown"),
                Permalink = Get("permalink")
            };
        }
        catch
        {
            return snapshot;
        }
    }

    private static string UpsertScalar(string raw, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return raw;

        value ??= string.Empty;
        var needsQuotes = value.Length == 0
                          || value.Any(c => char.IsWhiteSpace(c) || c is ':' or '#' or '{' or '}' or '[' or ']' or ',' or '&' or '*' or '?' or '|' or '>' or '!' or '%' or '@' or '`');
        var rendered = needsQuotes
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : value;

        var pattern = new Regex($@"(?m)^({Regex.Escape(key)}\s*:\s*).*$");
        if (pattern.IsMatch(raw))
            return pattern.Replace(raw, $"$1{rendered}", 1);

        if (!raw.EndsWith('\n') && raw.Length > 0)
            raw += Environment.NewLine;

        return raw + $"{key}: {rendered}{Environment.NewLine}";
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
}
