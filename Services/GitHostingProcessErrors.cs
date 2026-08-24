using Jekyller.Models;

namespace Jekyller.Services;

public static class GitHostingProcessErrors
{
    public static ProcessResult WithRepositoryAccessHint(
        GitHostingPlatform platform,
        string operation,
        ProcessResult result)
    {
        if (result.Success || platform == GitHostingPlatform.GitHub || !LooksLikeAccessFailure(result.CombinedOutput))
            return result;

        return new ProcessResult
        {
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr =
                $"{PlatformLabel(platform)} 無法{operation}：Git 命令列沒有此 repository 的讀寫權限，或目前 Git 憑證不是可存取的帳號。\n" +
                $"瀏覽器登入不等於 Git 命令列已登入；請用 Git Credential Manager 登入 {PlatformLabel(platform)}，或使用具有 read_repository / write_repository 權限的 Personal Access Token 後再重試。\n" +
                result.CombinedOutput
        };
    }

    private static bool LooksLikeAccessFailure(string output) =>
        ContainsAny(
            output,
            "could not be found or you don't have permission",
            "not found or you don't have permission",
            "repository not found",
            "Authentication failed",
            "HTTP Basic: Access denied",
            "Permission denied",
            "403",
            "401");

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string PlatformLabel(GitHostingPlatform platform) => platform switch
    {
        GitHostingPlatform.GitLab => "GitLab",
        GitHostingPlatform.Codeberg => "Codeberg",
        GitHostingPlatform.Bitbucket => "Bitbucket",
        _ => "GitHub"
    };
}
