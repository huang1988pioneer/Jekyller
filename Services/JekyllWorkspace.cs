namespace Jekyller.Services;

internal static class JekyllWorkspace
{
    private static readonly string[] GeneratedCacheDirectories =
    [
        ".jekyll-cache",
        ".sass-cache"
    ];

    public static ProcessResult? CleanGeneratedCaches(string projectPath, IProgress<string>? output = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "專案路徑無效，無法清理 Jekyll 快取。"
            };
        }

        var projectRoot = Path.GetFullPath(projectPath);
        foreach (var directoryName in GeneratedCacheDirectories)
        {
            var target = Path.GetFullPath(Path.Combine(projectRoot, directoryName));
            if (!IsInsideProject(projectRoot, target) || !Directory.Exists(target))
                continue;

            try
            {
                Directory.Delete(target, recursive: true);
                output?.Report($"已清理 {directoryName}。");
            }
            catch (Exception ex)
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr =
                        $"無法清理 {directoryName}：{ex.Message}{Environment.NewLine}" +
                        "請關閉正在使用此站台的預覽、同步或編輯程序後再試一次。"
                };
            }
        }

        return null;
    }

    private static bool IsInsideProject(string projectRoot, string target)
    {
        var root = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
