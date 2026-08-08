using System.Text;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IContentService
{
    Task<IReadOnlyList<ContentFile>> ListContentAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default);
    Task<ContentFile> CreatePostAsync(string projectPath, string title, string? categories = null, string? tags = null, CancellationToken cancellationToken = default);
    Task<ContentFile> CreatePageAsync(string projectPath, string title, string fileName, CancellationToken cancellationToken = default);
}

public sealed class ContentService : IContentService
{
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
                if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                files.Add(new ContentFile
                {
                    FullPath = file,
                    RelativePath = rel,
                    Name = Path.GetFileName(file),
                    Kind = kind,
                    LastWriteTime = File.GetLastWriteTime(file)
                });
            }
        }

        AddDir(Path.Combine(projectPath, "_posts"), ContentKind.Post);
        AddDir(Path.Combine(projectPath, "_drafts"), ContentKind.Draft);

        // Root-level and common page folders
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            files.Add(new ContentFile
            {
                FullPath = file,
                RelativePath = Path.GetFileName(file),
                Name = Path.GetFileName(file),
                Kind = ContentKind.Page,
                LastWriteTime = File.GetLastWriteTime(file)
            });
        }

        foreach (var sub in new[] { "pages", "_pages", "about" })
        {
            var dir = Path.Combine(projectPath, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                if (files.Any(f => f.FullPath == file)) continue;
                files.Add(new ContentFile
                {
                    FullPath = file,
                    RelativePath = rel,
                    Name = Path.GetFileName(file),
                    Kind = ContentKind.Page,
                    LastWriteTime = File.GetLastWriteTime(file)
                });
            }
        }

        return Task.FromResult<IReadOnlyList<ContentFile>>(
            files.OrderByDescending(f => f.LastWriteTime).ToList());
    }

    public Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);

    public Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false), cancellationToken);
    }

    public async Task<ContentFile> CreatePostAsync(
        string projectPath,
        string title,
        string? categories = null,
        string? tags = null,
        CancellationToken cancellationToken = default)
    {
        var posts = Path.Combine(projectPath, "_posts");
        Directory.CreateDirectory(posts);

        var slug = Slugify(title);
        var date = DateTime.Now;
        var fileName = $"{date:yyyy-MM-dd}-{slug}.md";
        var fullPath = Path.Combine(posts, fileName);

        var cats = string.IsNullOrWhiteSpace(categories) ? "[]" : FormatYamlList(categories);
        var tagList = string.IsNullOrWhiteSpace(tags) ? "[]" : FormatYamlList(tags);

        var content = $"""
                       ---
                       layout: post
                       title: "{EscapeYaml(title)}"
                       date: {date:yyyy-MM-dd HH:mm:ss} {date:zzz}
                       categories: {cats}
                       tags: {tagList}
                       ---

                       在這裡開始撰寫文章內容。

                       ## 標題範例

                       - 列表項目
                       - 另一個項目

                       ```bash
                       # 程式碼區塊
                       jekyll serve
                       ```
                       """;

        await SaveAsync(fullPath, content, cancellationToken).ConfigureAwait(false);

        return new ContentFile
        {
            FullPath = fullPath,
            RelativePath = $"_posts/{fileName}",
            Name = fileName,
            Kind = ContentKind.Post,
            LastWriteTime = DateTime.Now
        };
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
        var fullPath = Path.Combine(projectPath, fileName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var content = $"""
                       ---
                       layout: page
                       title: "{EscapeYaml(title)}"
                       permalink: /{Path.GetFileNameWithoutExtension(fileName)}/
                       ---

                       這是 **{title}** 頁面。
                       """;

        await SaveAsync(fullPath, content, cancellationToken).ConfigureAwait(false);

        return new ContentFile
        {
            FullPath = fullPath,
            RelativePath = fileName,
            Name = Path.GetFileName(fileName),
            Kind = ContentKind.Page,
            LastWriteTime = DateTime.Now
        };
    }

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
}
