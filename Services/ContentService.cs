using System.Text;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IContentService
{
    Task<IReadOnlyList<ContentFile>> ListContentAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default);
    Task DeleteAsync(string fullPath, CancellationToken cancellationToken = default);
    Task<ContentFile> CreatePostAsync(string projectPath, string title, string? categories = null, string? tags = null, bool draft = false, CancellationToken cancellationToken = default);
    string NextDefaultPostTitle(string projectPath);
    Task<ContentFile> CreatePageAsync(string projectPath, string title, string fileName, CancellationToken cancellationToken = default);
    Task<ContentFile> CreateTabAsync(string projectPath, string title, string? icon = null, CancellationToken cancellationToken = default);
}

public sealed partial class ContentService : IContentService
{
    private static readonly FrontMatterService FrontMatter = new();

    public Task<IReadOnlyList<ContentFile>> ListContentAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(projectPath))
            return Task.FromResult<IReadOnlyList<ContentFile>>([]);

        var files = new List<ContentFile>();
        void AddDir(string dir, ContentKind kind)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsContentFile(file)) continue;
                files.Add(ReadMetadata(projectPath, file, kind));
            }
        }

        AddDir(Path.Combine(projectPath, "_posts"), ContentKind.Post);
        AddDir(Path.Combine(projectPath, "_drafts"), ContentKind.Draft);
        AddDir(Path.Combine(projectPath, "_tabs"), ContentKind.Tab);

        foreach (var file in Directory.EnumerateFiles(projectPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (files.Any(f => PathsEqual(f.FullPath, file))) continue;
            files.Add(ReadMetadata(projectPath, file, ContentKind.Page));
        }

        foreach (var sub in new[] { "pages", "_pages", "about" })
        {
            var dir = Path.Combine(projectPath, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                if (files.Any(f => PathsEqual(f.FullPath, file))) continue;
                files.Add(ReadMetadata(projectPath, file, ContentKind.Page));
            }
        }

        return Task.FromResult<IReadOnlyList<ContentFile>>(
            files
                .OrderByDescending(f => f.ArticleDate.HasValue)
                .ThenByDescending(f => f.ArticleDate)
                .ThenByDescending(f => f.LastWriteTime)
                .ToList());
    }

    public Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);

    public Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false), cancellationToken);
    }

    public Task DeleteAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public async Task<ContentFile> CreatePostAsync(
        string projectPath,
        string title,
        string? categories = null,
        string? tags = null,
        bool draft = false,
        CancellationToken cancellationToken = default)
    {
        var folderName = draft ? "_drafts" : "_posts";
        var posts = Path.Combine(projectPath, folderName);
        Directory.CreateDirectory(posts);

        var date = DateTime.Now;
        if (string.IsNullOrWhiteSpace(title))
            title = NextDefaultPostTitle(projectPath, date);

        var slug = Slugify(title);
        var fileName = draft ? $"{slug}.md" : $"{date:yyyy-MM-dd}-{slug}.md";
        var fullPath = UniqueFilePath(Path.Combine(posts, fileName));
        fileName = Path.GetFileName(fullPath);

        var cats = string.IsNullOrWhiteSpace(categories) ? "[]" : FormatYamlList(categories);
        var tagList = string.IsNullOrWhiteSpace(tags) ? "[]" : FormatYamlList(tags);
        var published = draft ? "false" : "true";

        var content = $"""
                       ---
                       layout: post
                       title: "{EscapeYaml(title)}"
                       date: {date:yyyy-MM-dd HH:mm:ss} {date:zzz}
                       published: {published}
                       categories: {cats}
                       tags: {tagList}
                       toc: true
                       comments: true
                       ---

                       """;

        await SaveAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return ReadMetadata(projectPath, fullPath, draft ? ContentKind.Draft : ContentKind.Post);
    }

    public string NextDefaultPostTitle(string projectPath) =>
        NextDefaultPostTitle(projectPath, DateTime.Now);

    private string NextDefaultPostTitle(string projectPath, DateTime date)
    {
        var names = new List<string>();
        foreach (var folderName in new[] { "_posts", "_drafts" })
        {
            var folder = Path.Combine(projectPath, folderName);
            if (!Directory.Exists(folder))
                continue;

            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                if (!IsContentFile(file))
                    continue;
                names.Add(Path.GetFileNameWithoutExtension(file));
                try
                {
                    var title = ReadMetadata(projectPath, file, ContentKind.Post).ArticleTitle;
                    if (!string.IsNullOrWhiteSpace(title))
                        names.Add(title);
                }
                catch
                {
                    // Filename is enough when metadata cannot be read.
                }
            }
        }

        return PostNaming.NextDefaultTitle(names, date);
    }

    public async Task<ContentFile> CreatePageAsync(
        string projectPath,
        string title,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            fileName += ".md";

        fileName = fileName.Replace('\\', '/').TrimStart('/');
        var fullPath = UniqueFilePath(Path.Combine(projectPath, fileName.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var permalink = Path.GetFileNameWithoutExtension(fullPath);
        var content = $"""
                       ---
                       layout: page
                       title: "{EscapeYaml(title)}"
                       permalink: /{permalink}/
                       ---

                       這是 **{title}** 頁面。
                       """;

        await SaveAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return ReadMetadata(projectPath, fullPath, ContentKind.Page);
    }

    public async Task<ContentFile> CreateTabAsync(
        string projectPath,
        string title,
        string? icon = null,
        CancellationToken cancellationToken = default)
    {
        var tabs = Path.Combine(projectPath, "_tabs");
        Directory.CreateDirectory(tabs);

        var slug = Slugify(title);
        var fullPath = UniqueFilePath(Path.Combine(tabs, $"{slug}.md"));
        var order = Directory.EnumerateFiles(tabs, "*.md").Count() + 1;
        var iconName = string.IsNullOrWhiteSpace(icon) ? "fas fa-file-alt" : icon.Trim();

        var content = $"""
                       ---
                       title: "{EscapeYaml(title)}"
                       icon: {iconName}
                       order: {order}
                       ---

                       這是 **{title}** 導覽頁。
                       """;

        await SaveAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return ReadMetadata(projectPath, fullPath, ContentKind.Tab);
    }

    private static ContentFile ReadMetadata(string projectPath, string fullPath, ContentKind kind)
    {
        var rel = Path.GetRelativePath(projectPath, fullPath).Replace('\\', '/');
        var name = Path.GetFileName(fullPath);
        var lastWrite = File.GetLastWriteTime(fullPath);
        var title = string.Empty;
        DateTimeOffset? articleDate = ParseDateFromFileName(name);
        var isDraft = kind == ContentKind.Draft;

        try
        {
            var text = File.ReadAllText(fullPath, Encoding.UTF8);
            var document = FrontMatter.Parse(text);
            if (document.Fields.TryGetValue("title", out var parsedTitle))
                title = parsedTitle;
            if (document.Fields.TryGetValue("date", out var parsedDate)
                && DateTimeOffset.TryParse(parsedDate, out var date))
                articleDate = date;
            if (document.Fields.TryGetValue("published", out var published)
                && published.Equals("false", StringComparison.OrdinalIgnoreCase))
                isDraft = true;
            if (document.Fields.TryGetValue("draft", out var draft)
                && draft.Equals("true", StringComparison.OrdinalIgnoreCase))
                isDraft = true;
        }
        catch
        {
            // Keep filename metadata if the file cannot be parsed.
        }

        return new ContentFile
        {
            FullPath = fullPath,
            RelativePath = rel,
            Name = name,
            Kind = kind,
            LastWriteTime = lastWrite,
            ArticleDate = articleDate,
            ArticleTitle = title,
            IsDraft = isDraft
        };
    }

    private static bool IsContentFile(string file) =>
        file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseDateFromFileName(string fileName)
    {
        var match = FileDateRegex().Match(fileName);
        if (!match.Success) return null;
        return DateTimeOffset.TryParse(match.Groups[1].Value, out var date) ? date : null;
    }

    private static string UniqueFilePath(string fullPath)
    {
        if (!File.Exists(fullPath)) return fullPath;

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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string Slugify(string title)
    {
        var s = title.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\u4e00-\u9fff\-]+", string.Empty);
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "post" : s;
    }

    private static string EscapeYaml(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string FormatYamlList(string csv)
    {
        var items = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) return "[]";
        return "[" + string.Join(", ", items.Select(i => $"\"{EscapeYaml(i)}\"")) + "]";
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-")]
    private static partial Regex FileDateRegex();
}
