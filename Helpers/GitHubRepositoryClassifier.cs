namespace Jekyller.Helpers;

public static class GitHubRepositoryClassifier
{
    private static readonly HashSet<string> JekyllConfigFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "_config.yml", "_config.yaml", "_config.toml"
    };

    private static readonly HashSet<string> JekyllLayoutSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "_posts", "_drafts", "_layouts", "_includes", "_sass", "_data", "_tabs", "_pages"
    };

    private static readonly HashSet<string> InitialRepoFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "readme.md", "readme.txt", "readme.rst",
        "license", "license.md", "license.txt", "licence", "licence.md",
        "copying", "copying.md",
        ".gitignore", ".gitattributes", ".gitmodules",
        "code_of_conduct.md", "contributing.md", "security.md"
    };

    public static bool LooksLikeJekyll(IEnumerable<string> rootNames)
    {
        var names = Normalize(rootNames);
        if (names.Any(name => JekyllConfigFiles.Contains(name)))
            return true;

        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var hasGemfile = set.Contains("Gemfile");
        var hasLayout = names.Any(name => JekyllLayoutSignals.Contains(name));
        return hasGemfile && hasLayout;
    }

    /// <summary>
    /// Existing GitHub repos that "Create new repo" may safely reuse:
    /// a Jekyll site, an empty repo, or GitHub's default README/license starter.
    /// </summary>
    public static bool CanReuseExisting(IEnumerable<string> rootNames)
    {
        var names = Normalize(rootNames);
        if (LooksLikeJekyll(names)) return true;
        if (names.Count == 0) return true;

        return names.All(name =>
            InitialRepoFiles.Contains(name)
            || InitialRepoFiles.Contains(Path.GetFileNameWithoutExtension(name))
            || name.Equals(".github", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Normalize(IEnumerable<string> rootNames) =>
        rootNames
            .Select(name => name.Trim().TrimEnd('/', '\\'))
            .Where(name => name.Length > 0)
            .ToList();
}
