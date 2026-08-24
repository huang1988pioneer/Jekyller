using Jekyller.Models;
using Jekyller.Services;
using System.Net;

Assert(!GitPlatformSelection.ShouldAdoptRemote(
        GitHostingPlatform.GitLab,
        string.Empty,
        Valid(GitHostingPlatform.GitHub)),
    "an empty GitLab field must not adopt a GitHub origin");

Assert(GitPlatformSelection.ShouldAdoptRemote(
        GitHostingPlatform.GitLab,
        string.Empty,
        Valid(GitHostingPlatform.GitLab)),
    "an empty GitLab field may adopt a GitLab origin");

Assert(!GitPlatformSelection.ShouldAdoptRemote(
        GitHostingPlatform.GitLab,
        "https://gitlab.com/team/site",
        Valid(GitHostingPlatform.GitLab)),
    "a saved platform URL must not be overwritten");

var configuredGitLab = Valid(GitHostingPlatform.GitLab);
var remoteGitHub = Valid(GitHostingPlatform.GitHub);
var displayTarget = GitPlatformSelection.GetSelectedPlatformTarget(
    GitHostingPlatform.GitLab,
    configuredGitLab,
    remoteGitHub);
Assert(displayTarget.Platform == GitHostingPlatform.GitLab,
    "GitLab selection must display the GitLab configured target, not the GitHub origin");

Assert(!GitPlatformSelection.ShouldUseGitHubPagesApi(
        GitHostingPlatform.GitLab,
        displayTarget,
        remoteGitHub),
    "GitLab selection must not query or show GitHub Pages/Actions status from a GitHub origin");

Assert(GitPlatformSelection.ShouldUseGitHubPagesApi(
        GitHostingPlatform.GitHub,
        remoteGitHub,
        remoteGitHub),
    "GitHub selection with a GitHub origin may show GitHub Pages/Actions status");

var gitLabFailure = GitHostingProcessErrors.WithRepositoryAccessHint(
    GitHostingPlatform.GitLab,
    "推送",
    new ProcessResult
    {
        ExitCode = 128,
        StdErr = "remote: The project you were looking for could not be found or you don't have permission to view it.\nfatal: repository 'https://gitlab.com/group5923835/fengtusama.gitlab.io.git/' not found"
    });
Assert(gitLabFailure.CombinedOutput.Contains("GitLab 無法推送", StringComparison.Ordinal),
    "GitLab access failures should identify the GitLab operation");
Assert(gitLabFailure.CombinedOutput.Contains("Git 命令列沒有此 repository 的讀寫權限", StringComparison.Ordinal),
    "GitLab access failures should explain command-line credential permissions");

var gitHubFailure = GitHostingProcessErrors.WithRepositoryAccessHint(
    GitHostingPlatform.GitHub,
    "推送",
    new ProcessResult { ExitCode = 128, StdErr = "fatal: repository not found" });
Assert(!gitHubFailure.CombinedOutput.Contains("Git 命令列沒有此 repository 的讀寫權限", StringComparison.Ordinal),
    "GitHub failures should keep the existing GitHub-specific path");

var gitLabTarget = new GitHubRepositoryTarget
{
    IsValid = true,
    Platform = GitHostingPlatform.GitLab,
    Owner = "group5923835",
    Repository = "fengtusama.gitlab.io",
    CanonicalUrl = "https://gitlab.com/group5923835/fengtusama.gitlab.io.git"
};
Assert(
    GitHostingAccessChecks.LsRemoteHeadArguments(gitLabTarget)
        == "ls-remote --symref \"https://gitlab.com/group5923835/fengtusama.gitlab.io.git\" HEAD",
    "non-GitHub access check should verify the selected repository through git ls-remote");
Assert(
    GitHostingAccessChecks.PushDryRunArguments("main", "codeberg")
        == "push --dry-run -u codeberg HEAD:\"main\"",
    "non-GitHub push should verify write access with git push --dry-run before the real push");

var accessFailure = GitHostingAccessChecks.FromLsRemoteResult(
    gitLabTarget,
    new ProcessResult
    {
        ExitCode = 128,
        StdErr = "remote: The project you were looking for could not be found or you don't have permission to view it."
    });
Assert(!accessFailure.HasAccess, "failed GitLab ls-remote should block the App push flow early");
Assert(accessFailure.Message.Contains("GitLab 無法存取", StringComparison.Ordinal),
    "failed GitLab ls-remote should explain that the App needs command-line Git access");

var accessSuccess = GitHostingAccessChecks.FromLsRemoteResult(
    gitLabTarget,
    new ProcessResult { ExitCode = 0, StdOut = "ref: refs/heads/main\tHEAD" });
Assert(accessSuccess.HasAccess, "successful GitLab ls-remote should allow the App push flow");

Assert(PagesAccessStatus.TryCreateProtectedSiteMessage(
        HttpStatusCode.Found,
        new Uri("https://projects.gitlab.io/auth?domain=https://group5923835.gitlab.io"),
        out var authRedirectMessage),
    "GitLab Pages auth redirects should be classified as protected site access");
Assert(authRedirectMessage.Contains("導向 GitLab Pages 驗證", StringComparison.Ordinal),
    "GitLab Pages auth redirects should explain the GitLab Pages access-control state");

Assert(PagesAccessStatus.TryCreateProtectedSiteMessage(
        HttpStatusCode.Unauthorized,
        null,
        out var unauthorizedMessage),
    "HTTP 401 should be classified as protected site access");
Assert(unauthorizedMessage.Contains("HTTP 401", StringComparison.Ordinal),
    "HTTP 401 protected site messages should include the status code");

Assert(PagesAccessStatus.TryCreateProtectedSiteMessage(
        HttpStatusCode.Forbidden,
        null,
        out var forbiddenMessage),
    "HTTP 403 should be classified as protected site access");
Assert(forbiddenMessage.Contains("HTTP 403", StringComparison.Ordinal),
    "HTTP 403 protected site messages should include the status code");

var codebergPagesTarget = new GitHubRepositoryTarget
{
    IsValid = true,
    Platform = GitHostingPlatform.Codeberg,
    Owner = "fengtusama",
    Repository = "pages",
    CanonicalUrl = "https://codeberg.org/fengtusama/pages.git"
};
Assert(StaticPagesDeployment.ShouldPublishOutputBranch(codebergPagesTarget),
    "Codeberg Pages must publish the generated static output branch");
Assert(StaticPagesDeployment.OutputBranchFor(GitHostingPlatform.Codeberg) == "pages",
    "Codeberg Pages output branch must be pages");
Assert(!StaticPagesDeployment.ShouldPushSourceBranch(codebergPagesTarget),
    "Codeberg Pages must not require write access to the source main branch");
Assert(!StaticPagesDeployment.ShouldPublishOutputBranch(gitLabTarget),
    "GitLab should keep the CI/source repository flow");
Assert(StaticPagesDeployment.ShouldPushSourceBranch(gitLabTarget),
    "GitLab should keep pushing the source branch for CI");

Assert(GitLabPagesCi.Configuration.Contains("image: ruby:3.3", StringComparison.Ordinal),
    "GitLab Pages CI must use Ruby for Jekyll");
Assert(GitLabPagesCi.Configuration.Contains("bundle exec jekyll build -d public", StringComparison.Ordinal),
    "GitLab Pages CI must build Jekyll into public directory");
Assert(GitLabPagesCi.Configuration.Contains("pages:", StringComparison.Ordinal),
    "GitLab Pages CI must publish the public artifact as a Pages deployment");

Assert(DeploymentMarkerFiles.PrimaryFileName == "hugoer-deployment.json",
    "new deployments should use the current Hugoer product marker name");
Assert(DeploymentMarkerFiles.ReadCandidates.SequenceEqual(
        ["hugoer-deployment.json", "jekyller-deployment.json"]),
    "deployment monitoring should accept both the current and legacy marker names");
Assert(DeploymentMarkerFiles.OutputFileNames.Contains("jekyller-deployment.json", StringComparer.OrdinalIgnoreCase),
    "new static deployments should retain the legacy marker alias for existing monitors");
var expectedMarkerPaths = DeploymentMarkerFiles.ExpectedMarkerPaths("C:\\site").ToArray();
Assert(expectedMarkerPaths.Contains(Path.Combine("C:\\site", "static", "hugoer-deployment.json")),
    "Hugo source marker should be discoverable from the static directory");
Assert(expectedMarkerPaths.Contains(Path.Combine("C:\\site", "public", "hugoer-deployment.json")),
    "an already-built Hugo marker should remain discoverable from public output");

var outputRoot = Path.Combine(Path.GetTempPath(), "JekyllerStaticPagesDeploymentTests", Guid.NewGuid().ToString("N"));
try
{
    var publicDir = Path.Combine(outputRoot, "public");
    Directory.CreateDirectory(publicDir);
    File.WriteAllText(Path.Combine(publicDir, "index.html"), "<!doctype html>");
    Assert(StaticPagesDeployment.TryFindOutputDirectory(outputRoot, out var outputDir, out var outputMessage),
        "generated public output should be detected for Hugo sites");
    Assert(outputDir == publicDir, "public output directory should be preferred for Hugo sites");
    Assert(string.IsNullOrWhiteSpace(outputMessage), "output detection success should not include an error message");
}
finally
{
    if (Directory.Exists(outputRoot))
        Directory.Delete(outputRoot, recursive: true);
}

Console.WriteLine("GIT_PLATFORM_SELECTION_HARNESS_OK");

static GitHubRepositoryTarget Valid(GitHostingPlatform platform) => new()
{
    IsValid = true,
    Platform = platform
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
