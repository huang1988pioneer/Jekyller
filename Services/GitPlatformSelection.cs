using Jekyller.Models;

namespace Jekyller.Services;

public static class GitPlatformSelection
{
    public static bool ShouldAdoptRemote(
        GitHostingPlatform selectedPlatform,
        string? currentRepositoryUrl,
        GitHubRepositoryTarget remoteTarget) =>
        string.IsNullOrWhiteSpace(currentRepositoryUrl)
        && remoteTarget.IsValid
        && remoteTarget.Platform == selectedPlatform;

    public static GitHubRepositoryTarget GetSelectedPlatformTarget(
        GitHostingPlatform selectedPlatform,
        GitHubRepositoryTarget configuredTarget,
        GitHubRepositoryTarget remoteTarget)
    {
        if (configuredTarget.IsValid && configuredTarget.Platform == selectedPlatform)
            return configuredTarget;

        if (remoteTarget.IsValid && remoteTarget.Platform == selectedPlatform)
            return remoteTarget;

        return new GitHubRepositoryTarget { Platform = selectedPlatform };
    }

    public static bool ShouldUseGitHubPagesApi(
        GitHostingPlatform selectedPlatform,
        GitHubRepositoryTarget selectedTarget,
        GitHubRepositoryTarget remoteTarget) =>
        selectedPlatform == GitHostingPlatform.GitHub
        && selectedTarget.IsValid
        && remoteTarget.IsValid
        && selectedTarget.Platform == GitHostingPlatform.GitHub
        && remoteTarget.Platform == GitHostingPlatform.GitHub
        && IsSameRepository(selectedTarget, remoteTarget);

    public static bool IsSameRepository(GitHubRepositoryTarget left, GitHubRepositoryTarget right) =>
        left.IsValid
        && right.IsValid
        && left.Platform == right.Platform
        && string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Repository, right.Repository, StringComparison.OrdinalIgnoreCase);
}
