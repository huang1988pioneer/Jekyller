using System.Net;

namespace Jekyller.Services;

public static class PagesAccessStatus
{
    public const string GitLabPagesSettingsHint =
        "請到 GitLab 專案 Settings > Pages：" +
        "目前若是 Everyone With Access，只有有專案權限的帳號登入後才看得到網站；" +
        "要讓訪客直接開，請改成 Everyone。" +
        "GitLab Pages 會快取靜態檔，變更通常不到 1 分鐘就會生效。";

    public static string GitLabDeployedMessage(string? pagesUrl)
    {
        var urlLine = string.IsNullOrWhiteSpace(pagesUrl)
            ? "請在 GitLab 專案 Settings > Pages 查看網站網址。"
            : $"建議網站網址：{pagesUrl}";
        return
            "已推送到 GitLab。Pages CI 會用 Ruby / Bundler 建置 Jekyll 並發布。\n" +
            urlLine + "\n" +
            GitLabPagesSettingsHint;
    }

    public static string WithGitLabCacheHint(string? pagesUrl, string message)
    {
        if (string.IsNullOrWhiteSpace(pagesUrl)
            || !pagesUrl.Contains(".gitlab.io", StringComparison.OrdinalIgnoreCase)
            || message.Contains("不到 1 分鐘", StringComparison.Ordinal))
        {
            return message;
        }

        return message + " GitLab Pages 有快取，更新通常不到 1 分鐘就會生效。";
    }

    public static bool TryCreateProtectedSiteMessage(
        HttpStatusCode statusCode,
        Uri? location,
        out string message)
    {
        var code = (int)statusCode;
        if (IsAuthRedirect(statusCode, location))
        {
            message =
                "Pages 網站目前需要登入或尚未公開（導向 GitLab Pages 驗證）。" +
                GitLabPagesSettingsHint;
            return true;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            message =
                $"Pages 網站目前需要登入或尚未公開（HTTP {code}）。" +
                "若這是 GitLab Pages，" + GitLabPagesSettingsHint +
                "若維持 Everyone With Access 或私有，請用有專案權限的帳號登入後再開啟。";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool IsAuthRedirect(HttpStatusCode statusCode, Uri? location)
    {
        var code = (int)statusCode;
        if (code is not (301 or 302 or 303 or 307 or 308) || location is null)
            return false;

        var value = location.ToString();
        return value.Contains("/auth", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/oauth/authorize", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/users/sign_in", StringComparison.OrdinalIgnoreCase);
    }
}
