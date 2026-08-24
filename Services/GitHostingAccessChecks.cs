using Jekyller.Models;

namespace Jekyller.Services;

public static class GitHostingAccessChecks
{
    public static string LsRemoteHeadArguments(GitHubRepositoryTarget target) =>
        $"ls-remote --symref \"{target.CanonicalUrl}\" HEAD";

    public static string PushDryRunArguments(string remoteBranch, string remoteName = "origin") =>
        $"push --dry-run -u {remoteName} HEAD:\"{remoteBranch}\"";

    public static (bool HasAccess, string Message) FromLsRemoteResult(
        GitHubRepositoryTarget target,
        ProcessResult result)
    {
        if (result.Success)
            return (true,
                $"已確認 {target.PlatformLabel} repository 可由本機 Git 存取；接著會在程式內安全 fetch、合併並推送。");

        var hinted = GitHostingProcessErrors.WithRepositoryAccessHint(
            target.Platform,
            "存取",
            result);
        return (false, hinted.CombinedOutput);
    }
}
