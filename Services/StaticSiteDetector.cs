using Jekyller.Models;

namespace Jekyller.Services;

public static class StaticSiteDetector
{
    public static StaticSiteKind Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return StaticSiteKind.Unknown;
        if (LooksLikeHugo(path))
            return StaticSiteKind.Hugo;
        if (LooksLikeHexo(path))
            return StaticSiteKind.Hexo;
        if (LooksLikeJekyll(path))
            return StaticSiteKind.Jekyll;
        return StaticSiteKind.Unknown;
    }

    public static bool LooksLikeHugo(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        if (FileExistsAny(path, "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json"))
            return true;

        var configDir = Path.Combine(path, "config");
        if (Directory.Exists(configDir))
        {
            if (FileExistsAny(configDir, "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json"))
                return true;
            var defaults = Path.Combine(configDir, "_default");
            if (Directory.Exists(defaults)
                && FileExistsAny(defaults,
                    "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json",
                    "config.toml", "config.yaml", "config.yml", "config.json"))
                return true;
        }

        if (Directory.Exists(Path.Combine(path, "archetypes"))
            && Directory.Exists(Path.Combine(path, "content")))
            return true;

        return FileExistsAny(path, "config.toml", "config.yaml", "config.yml", "config.json")
               && Directory.Exists(Path.Combine(path, "content"));
    }

    public static bool LooksLikeHexo(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        if (Directory.Exists(Path.Combine(path, "source", "_posts")))
            return true;

        if (Directory.Exists(Path.Combine(path, "scaffolds"))
            && FileExistsAny(path, "_config.yml", "_config.yaml"))
            return true;

        var packageJson = Path.Combine(path, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                var text = File.ReadAllText(packageJson);
                if (text.Contains("\"hexo\"", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // Ignore unreadable package.json; other signals still apply.
            }
        }

        var config = FirstExisting(path, "_config.yml", "_config.yaml");
        if (config is not null && Directory.Exists(Path.Combine(path, "source")))
        {
            try
            {
                foreach (var line in File.ReadLines(config))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("source_dir:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("public_dir:", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // Fall through.
            }
        }

        return false;
    }

    public static bool LooksLikeJekyll(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        if (LooksLikeHugo(path) || LooksLikeHexo(path))
            return false;

        return FileExistsAny(path, "_config.yml", "_config.yaml")
               || Directory.Exists(Path.Combine(path, "_posts"))
               || File.Exists(Path.Combine(path, "Gemfile"));
    }

    public static string DisplayName(StaticSiteKind kind) => kind switch
    {
        StaticSiteKind.Jekyll => "Jekyll",
        StaticSiteKind.Hugo => "Hugo",
        StaticSiteKind.Hexo => "Hexo",
        _ => "未知"
    };

    public static bool IsSupportedMigration(StaticSiteKind from, StaticSiteKind to) =>
        (from, to) is
            (StaticSiteKind.Hugo, StaticSiteKind.Jekyll) or
            (StaticSiteKind.Hexo, StaticSiteKind.Jekyll) or
            (StaticSiteKind.Jekyll, StaticSiteKind.Hugo) or
            (StaticSiteKind.Jekyll, StaticSiteKind.Hexo);

    public static (string Title, string Message)? ProjectOpenWarning(string path)
    {
        var kind = Detect(path);
        if (kind is not (StaticSiteKind.Hugo or StaticSiteKind.Hexo))
            return null;

        var name = DisplayName(kind);
        return (
            $"偵測到 {name} 站台",
            $"{name} 的文章格式與資料夾結構和 Jekyll 不同。內容編輯器是給 Jekyll 用的，建議先到「網站遷移」轉成 Jekyll。仍要開啟這個資料夾嗎？");
    }

    public static IReadOnlyList<string> HugoConfigCandidates(string path) =>
    [
        Path.Combine(path, "hugo.toml"),
        Path.Combine(path, "hugo.yaml"),
        Path.Combine(path, "hugo.yml"),
        Path.Combine(path, "hugo.json"),
        Path.Combine(path, "config.toml"),
        Path.Combine(path, "config.yaml"),
        Path.Combine(path, "config.yml"),
        Path.Combine(path, "config.json"),
        Path.Combine(path, "config", "hugo.toml"),
        Path.Combine(path, "config", "hugo.yaml"),
        Path.Combine(path, "config", "_default", "hugo.toml"),
        Path.Combine(path, "config", "_default", "hugo.yaml"),
        Path.Combine(path, "config", "_default", "config.toml"),
        Path.Combine(path, "config", "_default", "config.yaml")
    ];

    private static bool FileExistsAny(string directory, params string[] names) =>
        names.Any(name => File.Exists(Path.Combine(directory, name)));

    private static string? FirstExisting(string directory, params string[] names) =>
        names.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
}
