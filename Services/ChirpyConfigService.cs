using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IChirpyConfigService
{
    Task<ChirpySettings> LoadAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string projectPath, ChirpySettings settings, CancellationToken cancellationToken = default);
}

public sealed class ChirpyConfigService(IConfigService configService) : IChirpyConfigService
{
    public async Task<ChirpySettings> LoadAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var path = configService.FindConfigPath(projectPath);
        var raw = path is not null && File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        return Parse(raw);
    }

    public async Task SaveAsync(string projectPath, ChirpySettings s, CancellationToken cancellationToken = default)
    {
        var path = configService.FindConfigPath(projectPath) ?? Path.Combine(projectPath, "_config.yml");
        var raw = File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false)
            : DefaultChirpySkeleton();

        raw = UpsertScalar(raw, "title", s.Title);
        raw = UpsertScalar(raw, "tagline", s.Tagline);
        raw = UpsertMultiline(raw, "description", s.Description);
        raw = UpsertScalar(raw, "url", s.Url);
        raw = UpsertScalar(raw, "baseurl", s.BaseUrl);
        raw = UpsertScalar(raw, "lang", s.Lang);
        raw = UpsertScalar(raw, "timezone", s.Timezone);
        raw = UpsertScalar(raw, "theme_mode", s.ThemeMode);
        raw = UpsertScalar(raw, "avatar", s.Avatar);
        raw = UpsertScalar(raw, "cdn", s.Cdn);
        raw = UpsertScalar(raw, "social_preview_image", s.SocialPreviewImage);
        raw = UpsertBool(raw, "toc", s.Toc);
        raw = UpsertScalar(raw, "paginate", s.Paginate.ToString(CultureInfo.InvariantCulture));

        raw = UpsertNested(raw, "github", "username", s.GithubUsername);
        raw = UpsertNested(raw, "twitter", "username", s.TwitterUsername);

        raw = UpsertNested(raw, "social", "name", s.SocialName);
        raw = UpsertNested(raw, "social", "email", s.SocialEmail);
        raw = UpsertNested(raw, "social", "fediverse_handle", s.FediverseHandle);
        raw = UpsertSocialLinks(raw, s.SocialLinks);

        raw = UpsertNested(raw, "webmaster_verifications", "google", s.GoogleVerification);
        raw = UpsertNested(raw, "webmaster_verifications", "bing", s.BingVerification);

        raw = UpsertNested(raw, "analytics", "google", "id", s.GoogleAnalyticsId);
        raw = UpsertNested(raw, "analytics", "goatcounter", "id", s.GoatCounterId);

        raw = UpsertNested(raw, "comments", "provider", s.CommentsProvider);
        raw = UpsertNested(raw, "comments", "disqus", "shortname", s.DisqusShortname);
        raw = UpsertNested(raw, "comments", "utterances", "repo", s.UtterancesRepo);
        raw = UpsertNested(raw, "comments", "utterances", "issue_term", s.UtterancesIssueTerm);
        raw = UpsertNested(raw, "comments", "giscus", "repo", s.GiscusRepo);
        raw = UpsertNested(raw, "comments", "giscus", "repo_id", s.GiscusRepoId);
        raw = UpsertNested(raw, "comments", "giscus", "category", s.GiscusCategory);
        raw = UpsertNested(raw, "comments", "giscus", "category_id", s.GiscusCategoryId);

        raw = UpsertNested(raw, "pwa", "enabled", s.PwaEnabled ? "true" : "false");
        raw = UpsertNested(raw, "pwa", "cache", "enabled", s.PwaCacheEnabled ? "true" : "false");
        raw = UpsertNested(raw, "assets", "self_host", "enabled", s.AssetsSelfHostEnabled ? "true" : "false");

        raw = UpsertNested(raw, "actions", "edit_post", "enabled", s.EditPostEnabled ? "true" : "false");
        raw = UpsertNested(raw, "actions", "edit_post", "url", s.EditPostUrl);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Normalize(raw), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static ChirpySettings Parse(string raw)
    {
        return new ChirpySettings
        {
            Title = GetTop(raw, "title"),
            Tagline = GetTop(raw, "tagline"),
            Description = GetDescription(raw),
            Url = GetTop(raw, "url"),
            BaseUrl = GetTop(raw, "baseurl"),
            Lang = GetTop(raw, "lang"),
            Timezone = GetTop(raw, "timezone"),
            ThemeMode = GetTop(raw, "theme_mode"),
            Avatar = GetTop(raw, "avatar"),
            Cdn = GetTop(raw, "cdn"),
            SocialPreviewImage = GetTop(raw, "social_preview_image"),
            Toc = ParseBool(GetTop(raw, "toc"), true),
            Paginate = int.TryParse(GetTop(raw, "paginate"), out var p) ? p : 10,

            GithubUsername = GetNested(raw, "github", "username"),
            TwitterUsername = GetNested(raw, "twitter", "username"),
            SocialName = GetNested(raw, "social", "name"),
            SocialEmail = GetNested(raw, "social", "email"),
            FediverseHandle = GetNested(raw, "social", "fediverse_handle"),
            SocialLinks = GetSocialLinks(raw),

            GoogleVerification = GetNested(raw, "webmaster_verifications", "google"),
            BingVerification = GetNested(raw, "webmaster_verifications", "bing"),

            GoogleAnalyticsId = GetNested(raw, "analytics", "google", "id"),
            GoatCounterId = GetNested(raw, "analytics", "goatcounter", "id"),

            CommentsProvider = GetNested(raw, "comments", "provider"),
            DisqusShortname = GetNested(raw, "comments", "disqus", "shortname"),
            UtterancesRepo = GetNested(raw, "comments", "utterances", "repo"),
            UtterancesIssueTerm = GetNested(raw, "comments", "utterances", "issue_term"),
            GiscusRepo = GetNested(raw, "comments", "giscus", "repo"),
            GiscusRepoId = GetNested(raw, "comments", "giscus", "repo_id"),
            GiscusCategory = GetNested(raw, "comments", "giscus", "category"),
            GiscusCategoryId = GetNested(raw, "comments", "giscus", "category_id"),

            PwaEnabled = ParseBool(GetNested(raw, "pwa", "enabled"), true),
            PwaCacheEnabled = ParseBool(GetNested(raw, "pwa", "cache", "enabled"), true),
            AssetsSelfHostEnabled = ParseBool(GetNested(raw, "assets", "self_host", "enabled"), false),

            EditPostEnabled = ParseBool(GetNested(raw, "actions", "edit_post", "enabled"), false),
            EditPostUrl = GetNested(raw, "actions", "edit_post", "url")
        };
    }

    private static string GetTop(string raw, string key)
    {
        var m = Regex.Match(raw, $@"(?m)^{Regex.Escape(key)}\s*:\s*(.*)$");
        return m.Success ? Unquote(m.Groups[1].Value) : string.Empty;
    }

    private static string GetDescription(string raw)
    {
        // description: >- block or scalar
        var block = Regex.Match(raw, @"(?ms)^description\s*:\s*>-?\s*\n((?:[ \t]+.*\n?)*)");
        if (block.Success)
        {
            var lines = block.Groups[1].Value.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Select(l => Regex.Replace(l, @"^[ \t]{2}", string.Empty))
                .Where(l => l.Length > 0 || true);
            return string.Join(" ", lines.Select(l => l.Trim())).Trim();
        }

        return GetTop(raw, "description");
    }

    private static string GetNested(string raw, params string[] path)
    {
        // Heuristic: find section then child key with indentation
        if (path.Length == 2)
        {
            var section = path[0];
            var key = path[1];
            var m = Regex.Match(raw,
                $@"(?ms)^{Regex.Escape(section)}\s*:\s*\n(?:[ \t]+.*\n)*?[ \t]+{Regex.Escape(key)}\s*:\s*(.*)$");
            if (m.Success) return Unquote(m.Groups[1].Value);

            // inline fallback
            m = Regex.Match(raw, $@"(?m)^\s+{Regex.Escape(key)}\s*:\s*(.*)$");
            // Prefer under section: scan lines
            return FindIndented(raw, path);
        }

        if (path.Length == 3)
            return FindIndented(raw, path);

        return FindIndented(raw, path);
    }

    private static string FindIndented(string raw, string[] path)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, $@"^{Regex.Escape(path[0])}\s*:"))
            {
                depth = 1;
                var indentStack = new[] { 0 };
                for (var j = i + 1; j < lines.Length && depth > 0; j++)
                {
                    var l = lines[j];
                    if (string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith('#'))
                        continue;

                    var indent = l.TakeWhile(c => c is ' ' or '\t').Count();
                    if (indent == 0 && !l.TrimStart().StartsWith('#'))
                        break;

                    var content = l.Trim();
                    var keyPart = content.Split(':', 2)[0].Trim();
                    var valPart = content.Contains(':') ? content[(content.IndexOf(':') + 1)..].Trim() : string.Empty;

                    if (depth < path.Length && keyPart == path[depth])
                    {
                        if (depth == path.Length - 1)
                            return Unquote(valPart);

                        depth++;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static string GetSocialLinks(string raw)
    {
        var m = Regex.Match(raw, @"(?ms)^social\s*:\s*\n(?:.*\n)*?[ \t]+links\s*:\s*\n((?:[ \t]+-\s+.*\n?)*)");
        if (!m.Success) return string.Empty;

        var urls = new List<string>();
        foreach (Match item in Regex.Matches(m.Groups[1].Value, @"-\s+(\S+)"))
            urls.Add(item.Groups[1].Value.Trim().Trim('"', '\''));
        return string.Join(Environment.NewLine, urls);
    }

    private static string UpsertSocialLinks(string raw, string linksMultiline)
    {
        var urls = (linksMultiline ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();

        var listYaml = urls.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, urls.Select(u => $"    - {u}"));

        // Ensure social: block exists
        if (!Regex.IsMatch(raw, @"(?m)^social\s*:"))
        {
            raw = raw.TrimEnd() + Environment.NewLine + "social:" + Environment.NewLine;
        }

        if (Regex.IsMatch(raw, @"(?ms)^social\s*:\s*\n(?:.*\n)*?[ \t]+links\s*:"))
        {
            if (string.IsNullOrEmpty(listYaml))
            {
                raw = ReplaceOnce(raw,
                    @"(?ms)(^[ \t]+links\s*:\s*\n)((?:[ \t]+-\s+.*\n?)*)",
                    "$1");
            }
            else
            {
                raw = ReplaceOnce(raw,
                    @"(?ms)(^[ \t]+links\s*:\s*\n)((?:[ \t]+-\s+.*\n?)*)",
                    $"$1{listYaml}{Environment.NewLine}");
            }

            return raw;
        }

        // Insert links under social
        if (string.IsNullOrEmpty(listYaml))
            return raw;

        raw = ReplaceOnce(raw,
            @"(?m)^(social\s*:\s*)$",
            $"$1{Environment.NewLine}  links:{Environment.NewLine}{listYaml}");

        if (!raw.Contains("links:", StringComparison.Ordinal))
        {
            raw = ReplaceOnce(raw,
                @"(?ms)^(social\s*:\s*\n)",
                $"$1  links:{Environment.NewLine}{listYaml}{Environment.NewLine}");
        }

        return raw;
    }

    private static string UpsertScalar(string raw, string key, string value)
    {
        value ??= string.Empty;
        var rendered = FormatScalar(value);
        var pattern = new Regex($@"(?m)^({Regex.Escape(key)}\s*:\s*).*$");
        if (pattern.IsMatch(raw))
            return pattern.Replace(raw, $"$1{rendered}", 1);

        return raw.TrimEnd() + Environment.NewLine + $"{key}: {rendered}" + Environment.NewLine;
    }

    private static string UpsertBool(string raw, string key, bool value)
        => UpsertScalar(raw, key, value ? "true" : "false");

    private static string UpsertMultiline(string raw, string key, string value)
    {
        value ??= string.Empty;
        // Use plain scalar for simplicity
        return UpsertScalar(raw, key, value.Replace("\r\n", " ").Replace("\n", " ").Trim());
    }

    private static string UpsertNested(string raw, string section, string key, string value)
        => UpsertNested(raw, [section, key], value);

    private static string UpsertNested(string raw, string section, string mid, string key, string value)
        => UpsertNested(raw, [section, mid, key], value);

    private static string UpsertNested(string raw, string[] path, string value)
    {
        value ??= string.Empty;
        var rendered = FormatScalar(value);

        // Ensure root section exists
        if (!Regex.IsMatch(raw, $@"(?m)^{Regex.Escape(path[0])}\s*:"))
        {
            raw = raw.TrimEnd() + Environment.NewLine + $"{path[0]}:" + Environment.NewLine;
        }

        // Walk / create intermediate maps and set leaf
        // Simpler approach: regex replace leaf under section block if present; else append under section
        if (path.Length == 2)
        {
            var section = path[0];
            var key = path[1];
            var sectionPattern = new Regex($@"(?ms)^({Regex.Escape(section)}\s*:\s*\n)((?:[ \t]+.*\n?)*)");
            if (sectionPattern.IsMatch(raw))
            {
                return sectionPattern.Replace(raw, m =>
                {
                    var header = m.Groups[1].Value;
                    var body = m.Groups[2].Value;
                    var keyPattern = new Regex($@"(?m)^([ \t]+){Regex.Escape(key)}\s*:.*$");
                    if (keyPattern.IsMatch(body))
                        body = keyPattern.Replace(body, $"  {key}: {rendered}", 1);
                    else
                        body = body.TrimEnd() + Environment.NewLine + $"  {key}: {rendered}" + Environment.NewLine;
                    return header + body;
                }, 1);
            }
        }

        if (path.Length == 3)
        {
            var section = path[0];
            var mid = path[1];
            var key = path[2];

            // Ensure mid exists under section
            if (!Regex.IsMatch(raw, $@"(?ms)^{Regex.Escape(section)}\s*:\s*\n(?:.*\n)*?[ \t]+{Regex.Escape(mid)}\s*:"))
            {
                raw = ReplaceOnce(raw,
                    $@"(?m)^({Regex.Escape(section)}\s*:\s*)$",
                    $"$1{Environment.NewLine}  {mid}:");
                if (!Regex.IsMatch(raw, $@"(?ms)^{Regex.Escape(section)}\s*:\s*\n(?:.*\n)*?[ \t]+{Regex.Escape(mid)}\s*:"))
                {
                    raw = ReplaceOnce(raw,
                        $@"(?ms)^({Regex.Escape(section)}\s*:\s*\n)",
                        $"$1  {mid}:{Environment.NewLine}");
                }
            }

            var midBlock = new Regex(
                $@"(?ms)^([ \t]+){Regex.Escape(mid)}\s*:\s*\n((?:[ \t]+.*\n?)*)");
            if (midBlock.IsMatch(raw))
            {
                return midBlock.Replace(raw, m =>
                {
                    var indent = m.Groups[1].Value;
                    var header = $"{indent}{mid}:" + Environment.NewLine;
                    var body = m.Groups[2].Value;
                    var childIndent = indent + "  ";
                    var keyPattern = new Regex($@"(?m)^[ \t]+{Regex.Escape(key)}\s*:.*$");
                    if (keyPattern.IsMatch(body))
                        body = keyPattern.Replace(body, $"{childIndent}{key}: {rendered}", 1);
                    else
                        body = body.TrimEnd() + Environment.NewLine + $"{childIndent}{key}: {rendered}" + Environment.NewLine;
                    return header + body;
                }, 1);
            }

            // mid is scalar empty "mid:" without children yet
            raw = new Regex($@"(?m)^([ \t]+){Regex.Escape(mid)}\s*:\s*$")
                .Replace(raw, m => $"{m.Groups[1].Value}{mid}:{Environment.NewLine}{m.Groups[1].Value}  {key}: {rendered}", 1);
        }

        return raw;
    }

    private static string ReplaceOnce(string input, string pattern, string replacement)
        => new Regex(pattern).Replace(input, replacement, 1);

    private static string FormatScalar(string value)
    {
        if (value is "true" or "false" || int.TryParse(value, out _))
            return value;

        if (value.Length == 0)
            return "\"\"";

        var needsQuotes = value.Any(c =>
            char.IsWhiteSpace(c) || c is ':' or '#' or '{' or '}' or '[' or ']' or ',' or '&' or '*' or '?' or '|' or '>' or '!' or '%' or '@' or '`');
        return needsQuotes
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : value;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        // strip inline comments for simple scalars
        if (!value.StartsWith('"') && !value.StartsWith('\''))
        {
            var hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) value = value[..hash].Trim();
        }

        if (value.Length >= 2)
        {
            if (value.StartsWith('"') && value.EndsWith('"'))
                return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (value.StartsWith('\'') && value.EndsWith('\''))
                return value[1..^1];
        }

        if (value is "~" or "null" or "Null" or "NULL")
            return string.Empty;

        return value;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value == "1";
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

    private static string DefaultChirpySkeleton() =>
        """
        theme: jekyll-theme-chirpy
        lang: en
        timezone: Asia/Taipei
        title: My Site
        tagline: ""
        description: ""
        url: ""
        baseurl: ""
        theme_mode: ""
        avatar: ""
        toc: true
        paginate: 10
        github:
          username: ""
        twitter:
          username: ""
        social:
          name: ""
          email: ""
          links:
        comments:
          provider: ""
        pwa:
          enabled: true
          cache:
            enabled: true
        """;
}
