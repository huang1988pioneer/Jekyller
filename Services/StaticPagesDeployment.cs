using Jekyller.Models;

namespace Jekyller.Services;

public static class StaticPagesDeployment
{
    public const string CodebergPagesBranch = "pages";
    public const string BitbucketWebsiteBranch = "main";

    public static bool ShouldPublishOutputBranch(GitHubRepositoryTarget target) =>
        target.IsValid
        && (target.Platform == GitHostingPlatform.Codeberg
            || (target.Platform == GitHostingPlatform.Bitbucket && target.IsUserOrOrganizationSite));

    public static bool ShouldPushSourceBranch(GitHubRepositoryTarget target) =>
        !ShouldPublishOutputBranch(target);

    public static string? OutputBranchFor(GitHostingPlatform platform) =>
        platform switch
        {
            GitHostingPlatform.Codeberg => CodebergPagesBranch,
            GitHostingPlatform.Bitbucket => BitbucketWebsiteBranch,
            _ => null
        };

    public static string? OutputBranchFor(GitHubRepositoryTarget target)
    {
        if (!target.IsValid)
            return null;
        if (target.Platform == GitHostingPlatform.Codeberg)
            return CodebergPagesBranch;
        if (target.Platform == GitHostingPlatform.Bitbucket && target.IsUserOrOrganizationSite)
            return BitbucketWebsiteBranch;
        return null;
    }

    public static bool TryValidateDeploymentTarget(GitHubRepositoryTarget target, out string error)
    {
        if (target.Platform == GitHostingPlatform.Bitbucket && !target.IsUserOrOrganizationSite)
        {
            error =
                "Bitbucket Cloud 靜態網站必須使用名為 <workspace>.bitbucket.io 的 repository。" +
                "請改連該 workspace 的 Bitbucket Pages repository，Jekyller 會把建置後的靜態檔推到預設分支。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryFindOutputDirectory(
        string projectPath,
        out string outputDirectory,
        out string message)
    {
        foreach (var name in new[] { "public", "_site" })
        {
            var path = Path.Combine(projectPath, name);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                outputDirectory = path;
                message = string.Empty;
                return true;
            }
        }

        outputDirectory = string.Empty;
        message = "找不到可發佈的靜態輸出目錄。請先完成建置，並確認已產生 public/（Hugo）或 _site/（Jekyll）。";
        return false;
    }
}
