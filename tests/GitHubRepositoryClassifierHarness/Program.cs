using Jekyller.Helpers;

Assert(GitHubRepositoryClassifier.LooksLikeJekyll(["_config.yml", "_posts", "Gemfile"]),
    "_config.yml must be recognized as a Jekyll repository.");
Assert(GitHubRepositoryClassifier.LooksLikeJekyll(["_config.yaml", "_layouts"]),
    "_config.yaml must be recognized as a Jekyll repository.");
Assert(GitHubRepositoryClassifier.LooksLikeJekyll(["Gemfile", "_posts", "_includes"]),
    "Gemfile + _posts must be recognized as a Jekyll repository.");
Assert(!GitHubRepositoryClassifier.LooksLikeJekyll(["README.md", "LICENSE"]),
    "A README starter must not look like Jekyll.");
Assert(!GitHubRepositoryClassifier.LooksLikeJekyll(["package.json", "src"]),
    "A Node project must not look like Jekyll.");
Assert(!GitHubRepositoryClassifier.LooksLikeJekyll(["Gemfile"]),
    "A Gemfile alone must not look like Jekyll.");

Assert(GitHubRepositoryClassifier.CanReuseExisting(["_config.yml", "_posts"]),
    "An existing Jekyll repository may be reused by Create new repo.");
Assert(GitHubRepositoryClassifier.CanReuseExisting([]),
    "An empty repository may be reused.");
Assert(GitHubRepositoryClassifier.CanReuseExisting(["README.md", "LICENSE", ".gitignore"]),
    "GitHub's default starter files may be reused.");
Assert(GitHubRepositoryClassifier.CanReuseExisting(["README", ".github"]),
    "README without extension and .github may be reused.");
Assert(!GitHubRepositoryClassifier.CanReuseExisting(["package.json", "src"]),
    "An unrelated existing repository must not be auto-reused.");
Assert(!GitHubRepositoryClassifier.CanReuseExisting(["README.md", "app.py"]),
    "A mixed non-Jekyll repository must not be auto-reused.");

Console.WriteLine("GITHUB_REPOSITORY_CLASSIFIER_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
