using System.Globalization;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public sealed class ConvertedArticle
{
    public required string Markdown { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public bool IsDraft { get; init; }
    public bool IsPage { get; init; }
    public string Title { get; init; } = string.Empty;
}

/// <summary>
/// Converts a Markdown article between Jekyll, Hugo, and Hexo front matter,
/// filenames, and common shortcodes / Liquid tags.
/// </summary>
public static partial class ArticleFormatConverter
{
    private static readonly FrontMatterService FrontMatter = new();
    private static readonly string[] PostSections = ["posts", "post", "blog", "news", "articles"];
    private static readonly string[] ImageKeys =
    [
        "image.path", "image", "images", "cover", "photos",
        "featured_image", "featuredImage", "thumbnail", "og_image", "banner"
    ];

    public static ConvertedArticle Convert(
        string markdown,
        string sourcePath,
        string? sourceRelativePath,
        ContentKind kind,
        StaticSiteKind from,
        StaticSiteKind to)
    {
        var fileName = Path.GetFileName(sourcePath);
        var document = FrontMatter.Parse(markdown ?? string.Empty);
        Normalize(document, sourcePath, sourceRelativePath, fileName, kind, from);
        document.Body = ConvertBody(document.Body, from, to);
        var isDraft = IsDraft(document, kind);
        var isPage = kind is ContentKind.Page or ContentKind.Tab;
        ApplyTarget(document, to, isDraft, isPage);
        document.Flavor = to switch
        {
            StaticSiteKind.Hugo => FrontMatterFlavor.Hugo,
            StaticSiteKind.Hexo => FrontMatterFlavor.Hexo,
            _ => FrontMatterFlavor.Jekyll
        };
        document.Delimiter = "---";

        var relative = TargetRelativePath(
            sourceRelativePath,
            fileName,
            document,
            to,
            isDraft,
            isPage);
        return new ConvertedArticle
        {
            Markdown = FrontMatter.Write(document),
            RelativePath = relative,
            FileName = Path.GetFileName(relative),
            IsDraft = isDraft,
            IsPage = isPage,
            Title = Get(document, "title")
        };
    }

    public static string RewriteRelativeMedia(string markdown, string publicPrefix)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(publicPrefix))
            return markdown;

        var prefix = publicPrefix.Replace('\\', '/').TrimEnd('/');
        return RelativeMediaRegex().Replace(markdown, match =>
        {
            var alt = match.Groups[1].Value;
            var target = match.Groups[2].Value.Trim().Trim('"', '\'');
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith('/')
                || target.StartsWith('#')
                || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("{{", StringComparison.Ordinal))
                return match.Value;

            var file = target.Replace('\\', '/').TrimStart('.', '/');
            return $"![{alt}]({prefix}/{file})";
        });
    }

    private static void Normalize(
        FrontMatterDocument document,
        string sourcePath,
        string? sourceRelativePath,
        string fileName,
        ContentKind kind,
        StaticSiteKind from)
    {
        var (fileDate, fileSlug) = ParseFileName(fileName);
        if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("_index.md", StringComparison.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(sourcePath));
            if (!string.IsNullOrWhiteSpace(folder)
                && !folder.Equals("content", StringComparison.OrdinalIgnoreCase)
                && !folder.Equals("source", StringComparison.OrdinalIgnoreCase)
                && !IsPostSection(folder)
                && folder is not ("_posts" or "_drafts" or "_tabs" or "_pages" or "pages"))
                fileSlug = Slugify(folder);
        }

        if (string.IsNullOrWhiteSpace(Get(document, "title")))
            document.Fields["title"] = HumanizeSlug(fileSlug);

        if (string.IsNullOrWhiteSpace(Get(document, "date")))
        {
            if (fileDate is { } dated)
                document.Fields["date"] = dated.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            else
            {
                try
                {
                    var stamp = File.GetLastWriteTime(sourcePath);
                    document.Fields["date"] = stamp.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                }
                catch
                {
                    document.Fields["date"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(Get(document, "slug")))
            document.Fields["slug"] = fileSlug;

        var image = FirstField(document, ImageKeys);
        if (!string.IsNullOrWhiteSpace(image))
        {
            var first = image.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? image;
            document.Fields["image"] = first;
            document.Fields["image.path"] = first;
        }

        if (string.IsNullOrWhiteSpace(Get(document, "permalink")))
        {
            var url = FirstField(document, "url", "path");
            if (!string.IsNullOrWhiteSpace(url))
                document.Fields["permalink"] = url;
        }

        if (IsDraft(document, kind))
        {
            document.Fields["draft"] = "true";
            document.Fields["published"] = "false";
        }

        _ = from;
        _ = sourceRelativePath;
    }

    private static void ApplyTarget(FrontMatterDocument document, StaticSiteKind to, bool isDraft, bool isPage)
    {
        FormatDate(document, to);

        switch (to)
        {
            case StaticSiteKind.Hugo:
                document.Fields.Remove("layout");
                document.Fields.Remove("published");
                var hugoUrl = FirstField(document, "url", "permalink");
                document.Fields.Remove("permalink");
                if (isDraft)
                    document.Fields["draft"] = "true";
                else
                    document.Fields.Remove("draft");
                if (!string.IsNullOrWhiteSpace(hugoUrl))
                    document.Fields["url"] = hugoUrl;
                else
                    document.Fields.Remove("url");

                var hugoImage = FirstField(document, "image.path", "image", "cover");
                if (!string.IsNullOrWhiteSpace(hugoImage))
                {
                    document.Fields["image"] = hugoImage;
                    document.Fields["images"] = hugoImage;
                }

                document.Fields.Remove("icon");
                document.Fields.Remove("order");
                break;

            case StaticSiteKind.Hexo:
                document.Fields.Remove("layout");
                document.Fields.Remove("draft");
                document.Fields.Remove("url");
                if (isDraft)
                    document.Fields["published"] = "false";
                else
                    document.Fields.Remove("published");
                var hexoImage = FirstField(document, "image.path", "image", "cover", "images");
                if (!string.IsNullOrWhiteSpace(hexoImage))
                {
                    var first = hexoImage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
                    document.Fields["image"] = first;
                    if (string.IsNullOrWhiteSpace(Get(document, "cover")))
                        document.Fields["cover"] = first;
                }

                document.Fields.Remove("icon");
                document.Fields.Remove("order");
                document.Fields.Remove("image.path");
                document.Fields.Remove("image.alt");
                break;

            default:
                if (!isPage)
                    document.Fields["layout"] = "post";
                else if (string.IsNullOrWhiteSpace(Get(document, "layout")))
                    document.Fields["layout"] = "page";
                document.Fields.Remove("draft");
                if (isDraft)
                    document.Fields["published"] = "false";
                else
                    document.Fields.Remove("published");
                if (string.IsNullOrWhiteSpace(Get(document, "permalink"))
                    && !string.IsNullOrWhiteSpace(Get(document, "url")))
                    document.Fields["permalink"] = Get(document, "url");
                document.Fields.Remove("url");
                document.Fields.Remove("images");
                document.Fields.Remove("cover");
                document.Fields.Remove("photos");
                document.Fields.Remove("featured_image");
                document.Fields.Remove("featuredImage");
                document.Fields.Remove("thumbnail");
                document.Fields.Remove("og_image");
                document.Fields.Remove("type");
                document.Fields.Remove("weight");
                document.Fields.Remove("menu");
                document.Fields.Remove("cascade");
                document.Fields.Remove("resources");
                document.Fields.Remove("outputs");
                document.Fields.Remove("headless");
                var jekyllImage = FirstField(document, "image.path", "image");
                if (!string.IsNullOrWhiteSpace(jekyllImage))
                {
                    var first = jekyllImage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
                    document.Fields["image"] = first;
                    document.Fields["image.path"] = first;
                }

                break;
        }
    }

    private static string TargetRelativePath(
        string? sourceRelativePath,
        string sourceFileName,
        FrontMatterDocument document,
        StaticSiteKind to,
        bool isDraft,
        bool isPage)
    {
        var slug = Slugify(FirstNonEmpty(Get(document, "slug"), ParseFileName(sourceFileName).Slug, "post"));
        var date = ParseDate(Get(document, "date")) ?? DateTimeOffset.Now;
        var datedName = $"{date:yyyy-MM-dd}-{slug}.md";
        var slugName = $"{slug}.md";

        if (!isPage)
        {
            return to switch
            {
                StaticSiteKind.Hugo => Combine("content/posts", datedName),
                StaticSiteKind.Hexo => Combine(isDraft ? "source/_drafts" : "source/_posts",
                    isDraft ? slugName : datedName),
                _ => Combine(isDraft ? "_drafts" : "_posts", isDraft ? slugName : datedName)
            };
        }

        var remainder = StripContentPrefix(sourceRelativePath, sourceFileName);
        if (remainder.EndsWith("/index.md", StringComparison.OrdinalIgnoreCase)
            || remainder.Equals("index.md", StringComparison.OrdinalIgnoreCase)
            || remainder.EndsWith("/_index.md", StringComparison.OrdinalIgnoreCase)
            || remainder.Equals("_index.md", StringComparison.OrdinalIgnoreCase))
        {
            var dir = remainder.Contains('/') ? remainder[..remainder.LastIndexOf('/')] : string.Empty;
            remainder = string.IsNullOrEmpty(dir) ? slugName : $"{dir}.md";
        }
        else if (string.IsNullOrWhiteSpace(remainder) || remainder.EndsWith('/'))
            remainder = slugName;
        else if (!remainder.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                 && !remainder.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            remainder = $"{remainder.TrimEnd('/')}.md";

        remainder = remainder.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(remainder))
            remainder = slugName;

        return to switch
        {
            StaticSiteKind.Hugo => Combine("content", remainder),
            StaticSiteKind.Hexo => Combine("source", remainder),
            _ => remainder
        };
    }

    public static string StripContentPrefix(string? sourceRelativePath, string sourceFileName)
    {
        var relative = (sourceRelativePath ?? sourceFileName).Replace('\\', '/').Trim('/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0)
            return sourceFileName;

        if (parts[0] is "content" or "source")
            parts.RemoveAt(0);
        if (parts.Count > 0
            && (IsPostSection(parts[0])
                || parts[0] is "_posts" or "_drafts" or "_tabs" or "_pages" or "pages" or "about"))
            parts.RemoveAt(0);

        return parts.Count == 0 ? sourceFileName : string.Join('/', parts);
    }

    public static bool IsPostSection(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && PostSections.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string ConvertBody(string body, StaticSiteKind from, StaticSiteKind to)
    {
        body ??= string.Empty;
        body = JekyllHighlightRegex().Replace(body, match =>
            Fence(match.Groups[1].Value, match.Groups[2].Value));
        body = HexoCodeblockRegex().Replace(body, match =>
        {
            var header = match.Groups[1].Value;
            var lang = "text";
            var langMatch = Regex.Match(header, @"lang:([A-Za-z0-9_+-]+)", RegexOptions.IgnoreCase);
            if (langMatch.Success)
                lang = langMatch.Groups[1].Value;
            else
            {
                var word = header.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(word) && !word.Contains(':', StringComparison.Ordinal))
                    lang = word;
            }

            return Fence(lang, match.Groups[2].Value);
        });
        body = HugoHighlightRegex().Replace(body, match =>
            Fence(match.Groups[1].Value, match.Groups[2].Value));
        body = RawBlockRegex().Replace(body, match => match.Groups[1].Value);

        body = JekyllBaseUrlRegex().Replace(body, string.Empty);
        body = JekyllRelativeUrlRegex().Replace(body, match => match.Groups[1].Value);
        body = HugoBaseUrlRegex().Replace(body, string.Empty);

        body = HugoFigureRegex().Replace(body, match =>
        {
            var attrs = match.Groups[1].Value;
            var src = ReadAttr(attrs, "src") ?? ReadAttr(attrs, "link") ?? string.Empty;
            var alt = ReadAttr(attrs, "alt") ?? ReadAttr(attrs, "caption") ?? string.Empty;
            var caption = ReadAttr(attrs, "caption");
            if (string.IsNullOrWhiteSpace(src))
                return match.Value;
            var image = $"![{alt}]({src})";
            return string.IsNullOrWhiteSpace(caption) || caption == alt
                ? image
                : $"{image}\n*{caption}*";
        });
        body = HugoYoutubeRegex().Replace(body, match =>
            $"https://www.youtube.com/watch?v={match.Groups[1].Value.Trim().Trim('"')}");
        body = HexoAssetImgRegex().Replace(body, match =>
            $"![{match.Groups[2].Value.Trim()}]({match.Groups[1].Value.Trim()})");
        body = HexoYoutubeRegex().Replace(body, match =>
            $"https://www.youtube.com/watch?v={match.Groups[1].Value.Trim()}");

        _ = to;
        _ = from;
        return body.TrimStart('\r', '\n');
    }

    private static string Fence(string language, string code)
    {
        language = string.IsNullOrWhiteSpace(language) ? "text" : language.Trim();
        code = (code ?? string.Empty).Trim('\r', '\n');
        return $"```{language}\n{code}\n```";
    }

    private static string? ReadAttr(string attrs, string name)
    {
        var match = Regex.Match(
            attrs,
            $@"\b{Regex.Escape(name)}\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;
        match = Regex.Match(
            attrs,
            $@"\b{Regex.Escape(name)}\s*=\s*([^\s]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }

    private static bool IsDraft(FrontMatterDocument document, ContentKind kind)
    {
        if (kind == ContentKind.Draft)
            return true;
        if (Get(document, "draft").Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (Get(document, "published").Equals("false", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static void FormatDate(FrontMatterDocument document, StaticSiteKind to)
    {
        var raw = Get(document, "date");
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            && !DateTimeOffset.TryParse(raw, out date))
            return;

        document.Fields["date"] = to switch
        {
            StaticSiteKind.Hugo => date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            StaticSiteKind.Hexo => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => $"{date:yyyy-MM-dd HH:mm:ss} {date:zzz}"
        };
    }

    public static (DateTimeOffset? Date, string Slug) ParseFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(stem) || stem is "index" or "_index")
            return (null, "post");

        var match = FileDateRegex().Match(stem);
        if (match.Success
            && DateTimeOffset.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return (date, Slugify(match.Groups[2].Value));

        return (null, Slugify(stem));
    }

    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            return date;
        return DateTimeOffset.TryParse(value, out date) ? date : null;
    }

    public static string Slugify(string title)
    {
        var s = (title ?? string.Empty).Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\u4e00-\u9fff\-]+", string.Empty);
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "post" : s;
    }

    private static string HumanizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug == "post")
            return "Untitled";
        return slug.Replace('-', ' ');
    }

    private static string Combine(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return fileName.Replace('\\', '/');
        return $"{directory.Trim('/').Replace('\\', '/')}/{fileName.Trim('/')}";
    }

    private static string Get(FrontMatterDocument document, string key) =>
        document.Fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static string FirstField(FrontMatterDocument document, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Get(document, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(.+)$")]
    private static partial Regex FileDateRegex();

    [GeneratedRegex(@"\{%\s*highlight\s+([A-Za-z0-9_+-]+)\s*%\}([\s\S]*?)\{%\s*endhighlight\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex JekyllHighlightRegex();

    [GeneratedRegex(@"\{%\s*codeblock([^%]*)%\}([\s\S]*?)\{%\s*endcodeblock\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex HexoCodeblockRegex();

    [GeneratedRegex(@"\{\{[<%]\s*highlight\s+([A-Za-z0-9_+-]+)\s*[>%]\}\}([\s\S]*?)\{\{[<%]\s*/highlight\s*[>%]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex HugoHighlightRegex();

    [GeneratedRegex(@"\{%\s*raw\s*%\}([\s\S]*?)\{%\s*endraw\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex RawBlockRegex();

    [GeneratedRegex(@"\{\{\s*site\.baseurl\s*\}\}")]
    private static partial Regex JekyllBaseUrlRegex();

    [GeneratedRegex(@"\{\{\s*['""]([^'""]+)['""]\s*\|\s*(?:relative_url|absolute_url)\s*\}\}")]
    private static partial Regex JekyllRelativeUrlRegex();

    [GeneratedRegex(@"\{\{\s*\.Site\.BaseURL\s*\}\}")]
    private static partial Regex HugoBaseUrlRegex();

    [GeneratedRegex(@"\{\{[<%]\s*figure\s+([^>%]+?)\s*[>%]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex HugoFigureRegex();

    [GeneratedRegex(@"\{\{[<%]\s*youtube\s+(?:id\s*=\s*)?[""']?([A-Za-z0-9_-]+)[""']?\s*[>%]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex HugoYoutubeRegex();

    [GeneratedRegex(@"\{%\s*asset_img\s+(\S+)\s+(.+?)\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex HexoAssetImgRegex();

    [GeneratedRegex(@"\{%\s*youtube\s+(\S+)\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex HexoYoutubeRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex RelativeMediaRegex();
}
