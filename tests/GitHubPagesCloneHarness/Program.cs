using Jekyller.Helpers;
using Jekyller.Models;

Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://alice.github.io/") == "https://github.com/alice/alice.github.io",
    "User site Pages URL must map to owner.github.io repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://alice.github.io/blog/") == "https://github.com/alice/blog",
    "Project site Pages URL must map to owner/repo.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("alice.github.io/notes") == "https://github.com/alice/notes",
    "Scheme-less Pages URL must be accepted.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://group5923835.gitlab.io/fengtusama.gitlab.io/")
       == "https://gitlab.com/group5923835/fengtusama.gitlab.io",
    "GitLab Pages URL must map to its GitLab repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://group.gitlab.io/subgroup/project/")
       == "https://gitlab.com/group/subgroup/project",
    "Nested GitLab Pages URL must keep the subgroup in the repository path.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("fengtusama.codeberg.page")
       == "https://codeberg.org/fengtusama/pages",
    "Scheme-less Codeberg Pages URL must map to the pages repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://fengtusama.codeberg.page/")
       == "https://codeberg.org/fengtusama/pages",
    "Codeberg user Pages URL must map to the pages repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://fengtusama.codeberg.page/notes/")
       == "https://codeberg.org/fengtusama/notes",
    "Codeberg project Pages URL must map to the project repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("fengtusama.bitbucket.io")
       == "https://bitbucket.org/fengtusama/fengtusama.bitbucket.io",
    "Scheme-less Bitbucket Pages URL must map to the workspace.bitbucket.io repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://fengtusama.bitbucket.io/")
       == "https://bitbucket.org/fengtusama/fengtusama.bitbucket.io",
    "Bitbucket static website URL must map to the workspace.bitbucket.io repository.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://github.com/alice/blog") is null,
    "github.com repository URLs are not Pages hosts.");
Assert(GitHubPagesUrl.TryConvertToRepositoryUrl("https://example.com") is null,
    "Unrelated hosts must be ignored.");

var parent = Path.Combine(Path.GetTempPath(), "jekyller-clone-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(parent);
try
{
    Assert(GitHubCloneDestination.TryCreatePath(parent, "my-site", out var dest, out var error),
        "A valid parent and folder name must resolve.");
    Assert(string.IsNullOrEmpty(error), "Valid destination must not report an error.");
    Assert(dest == Path.GetFullPath(Path.Combine(parent, "my-site")), "Destination must be parent/name.");
    Assert(GitHubCloneDestination.IsVacant(dest), "Missing destination is vacant.");

    Assert(!GitHubCloneDestination.TryCreatePath(parent, "../escape", out _, out _),
        "Folder names must not contain path separators.");
    var invalidName = $"bad{Path.GetInvalidFileNameChars().First(c => !char.IsControl(c))}name";
    Assert(!GitHubCloneDestination.TryCreatePath(parent, invalidName, out _, out _),
        "Folder names must not contain invalid file characters.");
    Assert(!GitHubCloneDestination.TryCreatePath("", "site", out _, out _),
        "Parent directory is required.");

    var occupied = Path.Combine(parent, "occupied");
    Directory.CreateDirectory(occupied);
    File.WriteAllText(Path.Combine(occupied, "keep.txt"), "x");
    Assert(!GitHubCloneDestination.IsVacant(occupied), "Non-empty folders are not vacant.");

    var empty = Path.Combine(parent, "empty");
    Directory.CreateDirectory(empty);
    Assert(GitHubCloneDestination.IsVacant(empty), "Empty folders are vacant.");
}
finally
{
    try { Directory.Delete(parent, true); } catch { /* ignore */ }
}

const string reposJson = """
    [
      {
        "name": "blog",
        "full_name": "alice/blog",
        "html_url": "https://github.com/alice/blog",
        "description": "my blog",
        "private": false,
        "has_pages": true,
        "owner": { "login": "alice" }
      },
      {
        "name": "alice.github.io",
        "full_name": "alice/alice.github.io",
        "html_url": "https://github.com/alice/alice.github.io",
        "private": true,
        "has_pages": false,
        "owner": { "login": "alice" }
      },
      {
        "name": "notes-app",
        "full_name": "alice/notes-app",
        "has_pages": false,
        "owner": { "login": "alice" }
      }
    ]
    """;

var sites = GitHubPagesSiteParser.Parse(reposJson, "alice");
Assert(sites.Count == 2, "Only Pages-enabled or owner.github.io repositories should be listed.");
Assert(sites.Any(site => site.FullName == "alice/blog" && site.PagesUrl == "https://alice.github.io/blog/"),
    "Project Pages URL must be derived.");
Assert(sites.Any(site => site.IsUserOrOrganizationSite && site.IsPrivate && site.Repository == "alice.github.io"),
    "User github.io repos must be included even when has_pages is false.");
Assert(sites.All(site => site.FullName != "alice/notes-app"),
    "Repositories without Pages must be excluded.");

const string slurped = """
    [
      [{ "name": "one", "full_name": "alice/one", "has_pages": true, "owner": { "login": "alice" } }],
      [{ "name": "two", "full_name": "alice/two", "has_pages": true, "html_url": "https://github.com/alice/two" }]
    ]
    """;
var slurpedSites = GitHubPagesSiteParser.Parse(slurped, "alice");
Assert(slurpedSites.Count == 2, "gh --slurp arrays of pages must be flattened.");

const string concatenated = """
    [{"name":"alpha","full_name":"alice/alpha","has_pages":true,"owner":{"login":"alice"}}]
    [{"name":"beta","full_name":"alice/beta","has_pages":true,"owner":{"login":"alice"}}]
    """;
var concatenatedSites = GitHubPagesSiteParser.Parse(concatenated, "alice");
Assert(concatenatedSites.Select(site => site.Repository).SequenceEqual(["alpha", "beta"]),
    "Concatenated paginated JSON arrays must be joined.");

Console.WriteLine("GITHUB_PAGES_CLONE_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
