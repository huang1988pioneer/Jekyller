namespace Jekyller.Helpers;

public static class GitHubCloneDestination
{
    public static bool TryCreatePath(
        string? parentDirectory,
        string? siteName,
        out string destinationPath,
        out string errorMessage)
    {
        destinationPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            errorMessage = "請選擇要複製到的本機父資料夾。";
            return false;
        }

        var name = siteName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "請輸入本機資料夾名稱。";
            return false;
        }

        if (name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/')
            || name.Contains('\\'))
        {
            errorMessage = "本機資料夾名稱包含無效字元。";
            return false;
        }

        try
        {
            destinationPath = Path.GetFullPath(Path.Combine(parentDirectory.Trim(), name));
        }
        catch (Exception)
        {
            errorMessage = "本機路徑無效。";
            destinationPath = string.Empty;
            return false;
        }

        if (destinationPath.Contains('"'))
        {
            errorMessage = "本機路徑不可包含引號。";
            destinationPath = string.Empty;
            return false;
        }

        return true;
    }

    public static bool IsVacant(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || !Directory.Exists(destinationPath))
            return true;

        return !Directory.EnumerateFileSystemEntries(destinationPath).Any();
    }
}
