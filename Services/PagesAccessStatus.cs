using System.Net;

namespace Jekyller.Services;

public static class PagesAccessStatus
{
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
                "若要讓訪客直接瀏覽，請到 GitLab 專案 Settings > General > Visibility, project features, permissions，將 Pages access control 設為 Everyone；設定通常約 1 分鐘後生效。";
            return true;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            message =
                $"Pages 網站目前需要登入或尚未公開（HTTP {code}）。" +
                "若這是 GitLab Pages，請確認專案或 Pages access control 允許 Everyone；若維持私有，請用有專案權限的帳號登入後再開啟。";
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
