using Jekyller.Models;

namespace Jekyller.Services;

public static class GitHostingProcessErrors
{
    public static ProcessResult WithRepositoryAccessHint(
        GitHostingPlatform platform,
        string operation,
        ProcessResult result)
    {
        if (result.Success || platform == GitHostingPlatform.GitHub)
            return result;

        if (LooksLikePlanOrQuotaLimit(result.CombinedOutput))
        {
            return new ProcessResult
            {
                ExitCode = result.ExitCode,
                StdOut = result.StdOut,
                StdErr =
                    $"{PlatformLabel(platform)} 儲存庫已被設為唯讀（HTTP 402）：帳號或 Workspace 已超過方案／使用者額度限制（例如 Bitbucket 免費版人數上限）。\n" +
                    $"請至 {PlatformLabel(platform)} 網站後台管理使用者權限、移除多餘成員或變更方案以恢復寫入權限。\n" +
                    result.CombinedOutput
            };
        }

        if (!LooksLikeAccessFailure(result.CombinedOutput))
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

    private static bool LooksLikePlanOrQuotaLimit(string output) =>
        ContainsAny(
            output,
            "402",
            "exceeded its user limit",
            "restricted to read only access",
            "Change your plan to restore write access",
            "quota exceeded");

    private static bool LooksLikeAccessFailure(string output) =>
        ContainsAny(
            output,
            "could not be found or you don't have permission",
            "not found or you don't have permission",
            "repository not found",
            "Authentication failed",
            "HTTP Basic: Access denied",
            "Permission denied",
            "not allowed to push to branch",
            "pre-receive hook declined",
            "protected branch",
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
