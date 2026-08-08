namespace Jekyller.Models;

public sealed class GitHubPagesStatus
{
    public bool IsEnabled { get; init; }
    public string Status { get; init; } = "unknown";
    public string HtmlUrl { get; init; } = string.Empty;
    public string SourceBranch { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string Cname { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Success { get; init; }
}
