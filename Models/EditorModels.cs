namespace Jekyller.Models;

public enum MarkdownEditorMode
{
    Wysiwyg,
    Source
}

public enum MarkdownPreviewKind
{
    Render,
    MarkdownOutput
}

public sealed class AppSettings
{
    public string? LastProjectPath { get; set; }
    public List<string> RecentProjects { get; set; } = [];
    public bool? AutoOpenLastProject { get; set; }
    public string MarkdownEditorMode { get; set; } = "Wysiwyg";
}
