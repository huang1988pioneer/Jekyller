using Jekyller.Models;

namespace Jekyller.Services;

public static class StaticPagesDeployment
{
    public const string CodebergPagesBranch = "pages";

    public static bool ShouldPublishOutputBranch(GitHubRepositoryTarget target) =>
        target.IsValid && target.Platform == GitHostingPlatform.Codeberg;

    public static bool ShouldPushSourceBranch(GitHubRepositoryTarget target) =>
        !ShouldPublishOutputBranch(target);

    public static string? OutputBranchFor(GitHostingPlatform platform) =>
        platform == GitHostingPlatform.Codeberg ? CodebergPagesBranch : null;

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
