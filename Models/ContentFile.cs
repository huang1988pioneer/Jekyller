namespace Jekyller.Models;

public sealed class ContentFile
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public required ContentKind Kind { get; init; }
    public DateTime LastWriteTime { get; init; }
    public DateTimeOffset? ArticleDate { get; init; }
    public string ArticleTitle { get; init; } = string.Empty;
    public bool IsDraft { get; init; }
    public bool IsDirectory { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(ArticleTitle) ? Name : ArticleTitle;
    public bool IsPublished => !IsDraft;
    public bool HasArticleDate => ArticleDate.HasValue;
    public string PublicationStatusText => Kind switch
    {
        ContentKind.Draft => "草稿",
        ContentKind.Tab => "導覽",
        ContentKind.Page => "頁面",
        _ => IsDraft ? "草稿" : "已發布"
    };
    public string TimelineText => ArticleDate.HasValue
        ? $"文章 {ArticleDate:yyyy/MM/dd} · 更新 {LastWriteTime:yyyy/MM/dd HH:mm}"
        : $"未設定日期 · 更新 {LastWriteTime:yyyy/MM/dd HH:mm}";

    public override string ToString() => RelativePath;
}

public enum ContentKind
{
    Post,
    Page,
    Draft,
    Tab,
    Other
}
