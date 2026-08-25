using System.Text;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public sealed class SiteMigrationRequest
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required StaticSiteKind SourceKind { get; init; }
    public required StaticSiteKind TargetKind { get; init; }
}

public sealed class SiteMigrationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Posts { get; init; }
    public int Pages { get; init; }
    public int Drafts { get; init; }
    public int Assets { get; init; }
    public string DestinationPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string Summary =>
        Success
            ? $"已遷移 {Posts} 篇文章、{Drafts} 篇草稿、{Pages} 個頁面、{Assets} 個靜態檔到 {DestinationPath}"
            : Message;
}

public interface ISiteMigrationService
{
    StaticSiteKind Detect(string path);
    bool IsSupported(StaticSiteKind from, StaticSiteKind to);
    Task<SiteMigrationResult> MigrateAsync(
        SiteMigrationRequest request,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default);
}

public sealed partial class SiteMigrationService : ISiteMigrationService
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", ".jekyll-cache", ".hugo_build.lock", ".hexo",
        ".vscode", ".idea", "_site", "public", "node_modules", "vendor",
        "themes", "resources", "bin", "obj"
    };
    private static readonly HashSet<string> SkipRootPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "license", "licence", "changelog", "contributing",
        "code_of_conduct", "security", "jekyller-migration", "copying"
    };

    public StaticSiteKind Detect(string path) => StaticSiteDetector.Detect(path);

    public bool IsSupported(StaticSiteKind from, StaticSiteKind to) =>
        StaticSiteDetector.IsSupportedMigration(from, to);

    public async Task<SiteMigrationResult> MigrateAsync(
        SiteMigrationRequest request,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        if (request.SourceKind == StaticSiteKind.Unknown)
            return Fail("無法辨識來源站台類型。請選擇含 Hugo、Hexo 或 Jekyll 設定檔的資料夾。");
        if (!IsSupported(request.SourceKind, request.TargetKind))
            return Fail("不支援這個遷移方向。目前支援 Hugo / Hexo → Jekyll，以及 Jekyll → Hugo / Hexo。");
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Directory.Exists(request.SourcePath))
            return Fail("找不到來源資料夾。");
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            return Fail("請指定目標資料夾。");

        var source = Path.GetFullPath(request.SourcePath);
        var destination = Path.GetFullPath(request.DestinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            return Fail("目標資料夾不能與來源相同。");
        if (IsInside(destination, source))
            return Fail("目標資料夾不能放在來源站台裡面。");

        Directory.CreateDirectory(destination);
        log?.Report($"來源：{StaticSiteDetector.DisplayName(request.SourceKind)}（{source}）");
        log?.Report($"目標：{StaticSiteDetector.DisplayName(request.TargetKind)}（{destination}）");

        var identity = ReadIdentity(source, request.SourceKind);
        var items = Collect(source, request.SourceKind);
        log?.Report($"找到 {items.Count} 個內容檔。");

        var posts = 0;
        var pages = 0;
        var drafts = 0;
        var assets = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string markdown;
            try
            {
                markdown = await File.ReadAllTextAsync(item.FullPath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warnings.Add($"無法讀取 {item.RelativePath}：{ex.Message}");
                log?.Report($"略過 {item.RelativePath}：{ex.Message}");
                continue;
            }

            var converted = ArticleFormatConverter.Convert(
                markdown,
                item.FullPath,
                item.RelativePath,
                item.Kind,
                request.SourceKind,
                request.TargetKind);

            converted = CopyBundleAssets(
                source,
                destination,
                item,
                converted,
                request.TargetKind,
                ref assets,
                log);

            var destFile = UniqueFilePath(Path.Combine(
                destination,
                converted.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            await File.WriteAllTextAsync(destFile, converted.Markdown, Utf8, cancellationToken)
                .ConfigureAwait(false);

            var written = Path.GetRelativePath(destination, destFile).Replace('\\', '/');
            log?.Report($"已轉換 {item.RelativePath} → {written}");
            if (converted.IsDraft)
                drafts++;
            else if (converted.IsPage)
                pages++;
            else
                posts++;
        }

        assets += CopyStaticAssets(source, destination, request.SourceKind, request.TargetKind, log);
        WriteTargetConfig(destination, request.TargetKind, identity);
        WriteMigrationNote(
            destination,
            request,
            identity,
            posts,
            pages,
            drafts,
            assets,
            warnings);

        log?.Report($"完成：{posts} 篇文章、{drafts} 篇草稿、{pages} 個頁面、{assets} 個靜態檔。");
        if (warnings.Count > 0)
            log?.Report($"注意：{warnings.Count} 則警告。");

        return new SiteMigrationResult
        {
            Success = true,
            Message = "遷移完成。",
            Posts = posts,
            Pages = pages,
            Drafts = drafts,
            Assets = assets,
            DestinationPath = destination,
            Warnings = warnings
        };
    }

    private static SiteMigrationResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };

    private static List<SourceItem> Collect(string source, StaticSiteKind kind) => kind switch
    {
        StaticSiteKind.Hugo => CollectHugo(source),
        StaticSiteKind.Hexo => CollectHexo(source),
        _ => CollectJekyll(source)
    };

    private static List<SourceItem> CollectJekyll(string source)
    {
        var items = new List<SourceItem>();
        AddMarkdownDir(items, source, "_posts", ContentKind.Post);
        AddMarkdownDir(items, source, "_drafts", ContentKind.Draft);
        AddMarkdownDir(items, source, "_tabs", ContentKind.Tab);
        AddMarkdownDir(items, source, "pages", ContentKind.Page);
        AddMarkdownDir(items, source, "_pages", ContentKind.Page);
        AddMarkdownDir(items, source, "about", ContentKind.Page);

        foreach (var file in EnumerateMarkdown(source, SearchOption.TopDirectoryOnly))
        {
            if (IsSkippableRootPage(file) || AlreadyAdded(items, file))
                continue;
            items.Add(CreateItem(source, file, ContentKind.Page));
        }

        return items;
    }

    private static List<SourceItem> CollectHugo(string source)
    {
        var items = new List<SourceItem>();
        var content = Path.Combine(source, "content");
        if (!Directory.Exists(content))
            return items;

        foreach (var file in EnumerateMarkdown(content, SearchOption.AllDirectories))
        {
            var relativeFromContent = Path.GetRelativePath(content, file).Replace('\\', '/');
            var name = Path.GetFileName(file);
            if (name.Equals("_index.md", StringComparison.OrdinalIgnoreCase))
            {
                var section = relativeFromContent.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(section)
                    && !relativeFromContent.Equals("_index.md", StringComparison.OrdinalIgnoreCase)
                    && ArticleFormatConverter.IsPostSection(section))
                    continue;
            }

            var kind = InferHugoKind(relativeFromContent, file);
            items.Add(CreateItem(source, file, kind));
        }

        return items;
    }

    private static ContentKind InferHugoKind(string relativeFromContent, string fullPath)
    {
        var first = relativeFromContent.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (ArticleFormatConverter.IsPostSection(first))
        {
            try
            {
                var text = File.ReadAllText(fullPath);
                var document = new FrontMatterService().Parse(text);
                if (document.Fields.TryGetValue("draft", out var draft)
                    && draft.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return ContentKind.Draft;
            }
            catch
            {
                // Filename classification is enough.
            }

            return ContentKind.Post;
        }

        var (date, _) = ArticleFormatConverter.ParseFileName(Path.GetFileName(fullPath));
        return date is null ? ContentKind.Page : ContentKind.Post;
    }

    private static List<SourceItem> CollectHexo(string source)
    {
        var items = new List<SourceItem>();
        var sourceDir = Path.Combine(source, "source");
        AddMarkdownDir(items, source, Path.Combine("source", "_posts"), ContentKind.Post);
        AddMarkdownDir(items, source, Path.Combine("source", "_drafts"), ContentKind.Draft);
        if (!Directory.Exists(sourceDir))
            return items;

        foreach (var file in EnumerateMarkdown(sourceDir, SearchOption.AllDirectories))
        {
            if (AlreadyAdded(items, file) || IsSkippableRootPage(file))
                continue;
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (relative.StartsWith("_posts/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase))
                continue;
            items.Add(CreateItem(source, file, ContentKind.Page));
        }

        return items;
    }

    private static void AddMarkdownDir(List<SourceItem> items, string source, string relativeDir, ContentKind kind)
    {
        var dir = Path.Combine(source, relativeDir);
        if (!Directory.Exists(dir))
            return;
        foreach (var file in EnumerateMarkdown(dir, SearchOption.AllDirectories))
            items.Add(CreateItem(source, file, kind));
    }

    private static IEnumerable<string> EnumerateMarkdown(string directory, SearchOption option)
    {
        if (!Directory.Exists(directory))
            yield break;
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", option))
        {
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }
    }

    private static SourceItem CreateItem(string source, string fullPath, ContentKind kind) => new()
    {
        FullPath = fullPath,
        RelativePath = Path.GetRelativePath(source, fullPath).Replace('\\', '/'),
        Kind = kind
    };

    private static bool AlreadyAdded(List<SourceItem> items, string fullPath) =>
        items.Any(item => string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private static bool IsSkippableRootPage(string fullPath)
    {
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        return SkipRootPages.Contains(stem);
    }

    private static ConvertedArticle CopyBundleAssets(
        string source,
        string destination,
        SourceItem item,
        ConvertedArticle converted,
        StaticSiteKind target,
        ref int assets,
        IProgress<string>? log)
    {
        var dir = Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrWhiteSpace(dir)
            || string.Equals(dir, source, StringComparison.OrdinalIgnoreCase))
            return converted;

        var isIndex = Path.GetFileName(item.FullPath).Equals("index.md", StringComparison.OrdinalIgnoreCase)
                      || Path.GetFileName(item.FullPath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);
        var siblings = Directory.EnumerateFiles(dir)
            .Where(file => !file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                           && !file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!isIndex && siblings.Count == 0)
            return converted;
        if (siblings.Count == 0)
            return converted;

        var slug = Path.GetFileNameWithoutExtension(converted.FileName);
        var (dated, datedSlug) = ArticleFormatConverter.ParseFileName(converted.FileName);
        if (dated is not null)
            slug = datedSlug;
        if (string.IsNullOrWhiteSpace(slug) || slug == "post")
            slug = ArticleFormatConverter.Slugify(Path.GetFileName(dir));

        var destFolder = BundleAssetFolder(destination, target, slug);
        Directory.CreateDirectory(destFolder);
        foreach (var file in siblings)
        {
            var destFile = Path.Combine(destFolder, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
            assets++;
            log?.Report($"已複製附件 {Path.GetFileName(file)}");
        }

        var prefix = $"/assets/img/{slug}";
        var rewritten = ArticleFormatConverter.RewriteRelativeMedia(converted.Markdown, prefix);
        return new ConvertedArticle
        {
            Markdown = rewritten,
            RelativePath = converted.RelativePath,
            FileName = converted.FileName,
            IsDraft = converted.IsDraft,
            IsPage = converted.IsPage,
            Title = converted.Title
        };
    }

    private static string BundleAssetFolder(string destination, StaticSiteKind target, string slug) => target switch
    {
        StaticSiteKind.Hugo => Path.Combine(destination, "static", "assets", "img", slug),
        StaticSiteKind.Hexo => Path.Combine(destination, "source", "assets", "img", slug),
        _ => Path.Combine(destination, "assets", "img", slug)
    };

    private static int CopyStaticAssets(
        string source,
        string destination,
        StaticSiteKind from,
        StaticSiteKind to,
        IProgress<string>? log)
    {
        var count = 0;
        switch (from)
        {
            case StaticSiteKind.Jekyll:
                count += CopyTree(
                    Path.Combine(source, "assets"),
                    AssetRoot(destination, to),
                    log);
                foreach (var extra in new[] { "images", "files", "uploads" })
                    count += CopyTree(Path.Combine(source, extra), StaticRoot(destination, to, extra), log);
                break;
            case StaticSiteKind.Hugo:
                count += CopyTree(
                    Path.Combine(source, "static"),
                    to == StaticSiteKind.Jekyll
                        ? destination
                        : to == StaticSiteKind.Hexo
                            ? Path.Combine(destination, "source")
                            : Path.Combine(destination, "static"),
                    log);
                count += CopyTree(Path.Combine(source, "assets"), AssetRoot(destination, to), log);
                break;
            case StaticSiteKind.Hexo:
                count += CopyHexoSourceFiles(Path.Combine(source, "source"), destination, to, log);
                break;
        }

        return count;
    }

    private static string AssetRoot(string destination, StaticSiteKind to) => to switch
    {
        StaticSiteKind.Hugo => Path.Combine(destination, "static", "assets"),
        StaticSiteKind.Hexo => Path.Combine(destination, "source", "assets"),
        _ => Path.Combine(destination, "assets")
    };

    private static string StaticRoot(string destination, StaticSiteKind to, string folder) => to switch
    {
        StaticSiteKind.Hugo => Path.Combine(destination, "static", folder),
        StaticSiteKind.Hexo => Path.Combine(destination, "source", folder),
        _ => Path.Combine(destination, folder)
    };

    private static int CopyHexoSourceFiles(
        string sourceDir,
        string destination,
        StaticSiteKind to,
        IProgress<string>? log)
    {
        if (!Directory.Exists(sourceDir))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(sourceDir, file))
                continue;
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            string destFile;
            if (relative.StartsWith("_posts/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = relative[(relative.IndexOf('/') + 1)..];
                destFile = Path.Combine(AssetRoot(destination, to), "img", rest.Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                destFile = Path.Combine(
                    to == StaticSiteKind.Hugo
                        ? Path.Combine(destination, "static")
                        : to == StaticSiteKind.Hexo
                            ? Path.Combine(destination, "source")
                            : destination,
                    relative.Replace('/', Path.DirectorySeparatorChar));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
            count++;
            log?.Report($"已複製 {relative}");
        }

        return count;
    }

    private static int CopyTree(string from, string to, IProgress<string>? log)
    {
        if (!Directory.Exists(from))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(from, file))
                continue;
            var relative = Path.GetRelativePath(from, file);
            var destFile = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
            count++;
            log?.Report($"已複製 {relative.Replace('\\', '/')}");
        }

        return count;
    }

    private static bool ShouldSkipPath(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(SkipDirectoryNames.Contains);
    }

    private static SiteIdentity ReadIdentity(string source, StaticSiteKind kind)
    {
        var files = kind switch
        {
            StaticSiteKind.Hugo => StaticSiteDetector.HugoConfigCandidates(source),
            _ => new[]
            {
                Path.Combine(source, "_config.yml"),
                Path.Combine(source, "_config.yaml")
            }
        };

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;
            try
            {
                MergeConfig(fields, File.ReadAllText(file));
            }
            catch
            {
                // Try the next config candidate.
            }
        }

        var rawUrl = First(fields, "baseURL", "baseurl", "url");
        var (url, baseUrl) = SplitSiteUrl(rawUrl);
        if (kind == StaticSiteKind.Jekyll)
        {
            url = First(fields, "url");
            baseUrl = First(fields, "baseurl", "baseURL");
        }

        var title = First(fields, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return new SiteIdentity
        {
            Title = title,
            Description = First(fields, "description", "subtitle"),
            Url = url,
            BaseUrl = baseUrl,
            Language = First(fields, "languageCode", "defaultContentLanguage", "language", "lang"),
            Timezone = First(fields, "timeZone", "timezone")
        };
    }

    private static void MergeConfig(IDictionary<string, string> fields, string text)
    {
        foreach (Match match in JsonPairRegex().Matches(text))
            fields.TryAdd(match.Groups[1].Value, UnquoteConfig(match.Groups[2].Value));

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            var eq = line.IndexOf('=');
            var colon = line.IndexOf(':');
            string key;
            string value;
            if (eq > 0 && (colon < 0 || eq < colon))
            {
                key = line[..eq].Trim().Trim('"');
                value = UnquoteConfig(line[(eq + 1)..]);
            }
            else if (colon > 0)
            {
                key = line[..colon].Trim().Trim('"');
                value = UnquoteConfig(line[(colon + 1)..]);
            }
            else
                continue;

            if (key.Length == 0 || key.Contains(' ') || key.StartsWith('['))
                continue;
            fields.TryAdd(key, value);
        }
    }

    private static string UnquoteConfig(string value)
    {
        value = value.Trim().TrimEnd(',').Trim();
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static (string Url, string BaseUrl) SplitSiteUrl(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return (value.TrimEnd('/'), string.Empty);

        var path = uri.AbsolutePath.TrimEnd('/');
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return string.IsNullOrEmpty(path) || path == "/"
            ? (origin, string.Empty)
            : (origin, path);
    }

    private static string First(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static void WriteTargetConfig(string destination, StaticSiteKind to, SiteIdentity identity)
    {
        Directory.CreateDirectory(destination);
        var title = QuoteYaml(identity.Title);
        var description = QuoteYaml(identity.Description);
        var url = string.IsNullOrWhiteSpace(identity.Url) ? "http://example.org" : identity.Url.TrimEnd('/');
        var baseUrl = identity.BaseUrl ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(baseUrl) && !baseUrl.StartsWith('/'))
            baseUrl = "/" + baseUrl;
        var language = string.IsNullOrWhiteSpace(identity.Language) ? "zh-TW" : identity.Language;
        var timezone = string.IsNullOrWhiteSpace(identity.Timezone) ? "Asia/Taipei" : identity.Timezone;

        switch (to)
        {
            case StaticSiteKind.Hugo:
                var hugoUrl = string.IsNullOrWhiteSpace(baseUrl) ? url + "/" : url + baseUrl.TrimEnd('/') + "/";
                File.WriteAllText(Path.Combine(destination, "hugo.toml"),
                    $"""
                     baseURL = '{EscapeToml(hugoUrl)}'
                     languageCode = '{EscapeToml(language.ToLowerInvariant())}'
                     title = '{EscapeToml(identity.Title)}'

                     [params]
                       description = '{EscapeToml(identity.Description)}'
                     """, Utf8);
                break;
            case StaticSiteKind.Hexo:
                File.WriteAllText(Path.Combine(destination, "_config.yml"),
                    $"""
                     title: {title}
                     description: {description}
                     language: {language}
                     timezone: {timezone}
                     url: {url}
                     root: {(string.IsNullOrWhiteSpace(baseUrl) ? "/" : baseUrl.TrimEnd('/') + "/")}
                     permalink: :year/:month/:day/:title/
                     source_dir: source
                     public_dir: public
                     """, Utf8);
                File.WriteAllText(Path.Combine(destination, "package.json"),
                    """
                    {
                      "name": "hexo-site",
                      "private": true,
                      "hexo": {},
                      "dependencies": {
                        "hexo": "^7.3.0"
                      }
                    }
                    """, Utf8);
                break;
            default:
                File.WriteAllText(Path.Combine(destination, "_config.yml"),
                    $"""
                     title: {title}
                     description: {description}
                     url: {url}
                     baseurl: "{EscapeYaml(baseUrl)}"
                     lang: {language}
                     timezone: {timezone}
                     markdown: kramdown
                     permalink: pretty
                     """, Utf8);
                File.WriteAllText(Path.Combine(destination, "Gemfile"),
                    """
                    source "https://rubygems.org"
                    gem "jekyll", "~> 4.3"
                    """, Utf8);
                break;
        }
    }

    private static void WriteMigrationNote(
        string destination,
        SiteMigrationRequest request,
        SiteIdentity identity,
        int posts,
        int pages,
        int drafts,
        int assets,
        IReadOnlyList<string> warnings)
    {
        var warningBlock = warnings.Count == 0
            ? string.Empty
            : Environment.NewLine + "## 警告" + Environment.NewLine + string.Join(Environment.NewLine, warnings.Select(item => "- " + item));
        File.WriteAllText(
            Path.Combine(destination, "JEKYLLER-MIGRATION.md"),
            $"""
             # Jekyller 網站遷移

             - 來源：{StaticSiteDetector.DisplayName(request.SourceKind)}（`{request.SourcePath}`）
             - 目標：{StaticSiteDetector.DisplayName(request.TargetKind)}（`{destination}`）
             - 站名：{identity.Title}
             - 文章：{posts}
             - 草稿：{drafts}
             - 頁面：{pages}
             - 靜態檔：{assets}

             主題、佈景與外掛無法自動轉換，請在目標引擎另外安裝。
             Liquid / Hugo shortcode / Hexo tag 已盡量轉成 Markdown；無法對應的標記會留在正文裡。
             {warningBlock}
             """,
            Utf8);
    }

    private static string QuoteYaml(string value)
    {
        value ??= string.Empty;
        return $"\"{EscapeYaml(value)}\"";
    }

    private static string EscapeYaml(string value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeToml(string value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

    private static string UniqueFilePath(string fullPath)
    {
        if (!File.Exists(fullPath))
            return fullPath;

        var dir = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(dir, $"{stem}-{index}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static bool IsInside(string inner, string outer)
    {
        var a = Path.GetFullPath(inner).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(outer).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"")]
    private static partial Regex JsonPairRegex();

    private sealed class SourceItem
    {
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required ContentKind Kind { get; init; }
    }

    private sealed class SiteIdentity
    {
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public string Timezone { get; init; } = string.Empty;
    }
}
