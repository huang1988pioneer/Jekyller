namespace Jekyller.Models;

public sealed class GitHubPagesStatus
{
    public bool IsEnabled { get; init; }
    public string Status { get; init; } = "unknown";
    public string HtmlUrl { get; init; } = string.Empty;
    public string SourceBranch { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string BuildType { get; init; } = string.Empty;
    public string Cname { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Success { get; init; }
}

public enum DeploymentVersionState
{
    NotConfigured,
    Previous,
    Latest,
    Unavailable
}

public sealed class DeploymentMarker
{
    public string DeploymentId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class DeploymentCheckResult
{
    public DeploymentVersionState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ExpectedDeploymentId { get; init; }
    public string? LiveDeploymentId { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
}

public sealed class GitRemoteInfo
{
    public string? RemoteUrl { get; set; }
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public string? Branch { get; set; }
    public bool GhAuthenticated { get; set; }
    public string? GhUser { get; set; }
}

public sealed class GitHubRepositoryTarget
{
    public GitHostingPlatform Platform { get; init; } = GitHostingPlatform.GitHub;
    public string PlatformLabel => Platform switch
    {
        GitHostingPlatform.GitLab => "GitLab",
        GitHostingPlatform.Codeberg => "Codeberg",
        GitHostingPlatform.Bitbucket => "Bitbucket",
        _ => "GitHub"
    };
    public bool IsValid { get; init; }
    public string? Owner { get; init; }
    public string? Repository { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? PagesUrl { get; init; }
    public string? JekyllUrl { get; init; }
    public string? JekyllBaseUrl { get; init; }
    public bool IsUserOrOrganizationSite { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

public enum GitHostingPlatform
{
    GitHub,
    GitLab,
    Codeberg,
    Bitbucket
}

public sealed class GitHubRepositoryLookup
{
    public bool CheckSucceeded { get; init; }
    public bool Exists { get; init; }
    public bool CanReuse { get; init; }
    public bool LooksLikeJekyll { get; init; }
    public GitHubRepositoryTarget? Target { get; init; }
    public string Message { get; init; } = string.Empty;

    public static GitHubRepositoryLookup Fail(string message) => new()
    {
        CheckSucceeded = false,
        Message = message
    };

    public static GitHubRepositoryLookup Missing() => new()
    {
        CheckSucceeded = true,
        Exists = false
    };
}

public sealed class GitHubPagesSiteItem
{
    public string Owner { get; init; } = string.Empty;
    public string Repository { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string PagesUrl { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsPrivate { get; init; }
    public bool IsUserOrOrganizationSite { get; init; }
    public bool HasPages { get; init; }

    public string KindLabel => IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站";
    public string VisibilityLabel => IsPrivate ? "Private" : "Public";
    public string Summary
    {
        get
        {
            var detail = string.IsNullOrWhiteSpace(Description) ? KindLabel : Description.Trim();
            return string.IsNullOrWhiteSpace(PagesUrl) ? detail : $"{PagesUrl}  ·  {detail}";
        }
    }
}

public sealed class GitHubPagesSitesResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<GitHubPagesSiteItem> Sites { get; init; } = [];

    public static GitHubPagesSitesResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
