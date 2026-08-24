using System.Text.Json;
using System.Text.RegularExpressions;
using Jekyller.Helpers;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IGitHubService
{
    Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> IsGhAvailableAsync(CancellationToken cancellationToken = default);
    Task<GitRemoteInfo> GetInfoAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        CancellationToken cancellationToken = default);
    Task<(bool HasAccess, string Message)> CheckPushAccessAsync(
        GitHubRepositoryTarget target,
        CancellationToken cancellationToken = default);
    Task<GitHubRepositoryLookup> LookupOwnedRepositoryAsync(
        string repoName,
        CancellationToken cancellationToken = default);
    Task<GitHubPagesSitesResult> ListPagesSitesAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> CloneRepositoryAsync(
        GitHubRepositoryTarget target,
        string destinationPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task UpdateSiteUrlsAsync(
        string projectPath,
        GitHubRepositoryTarget target,
        CancellationToken cancellationToken = default);
    Task<string> EnsureGitHubActionsWorkflowAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> InitRepositoryAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> CreateRepoAndPushAsync(
        string projectPath,
        string repoName,
        bool isPrivate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> ConnectExistingRepositoryAndPushAsync(
        string projectPath,
        GitHubRepositoryTarget target,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> PushAsync(
        string projectPath,
        string commitMessage,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> PullAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string> GetLatestDeploymentAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<GitHubPagesStatus> GetPagesStatusAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ProcessResult> EnablePagesFromActionsAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        bool allowManualSetupIfPushCompleted = false);
    Task<string?> DetectRemoteAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        CancellationToken cancellationToken = default);
    Task<(string Owner, string Repo)?> ParseOwnerRepoAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ProcessResult> OpenGhAuthLoginAsync(CancellationToken cancellationToken = default);
}

public sealed partial class GitHubService : IGitHubService
{
    private readonly IProcessRunner _processRunner;
    private readonly IConfigService _configService;
    private readonly DeploymentMonitorService _deploymentMonitor;

    public GitHubService(
        IProcessRunner processRunner,
        IConfigService configService,
        DeploymentMonitorService? deploymentMonitor = null)
    {
        _processRunner = processRunner;
        _configService = configService;
        _deploymentMonitor = deploymentMonitor ?? new DeploymentMonitorService();
    }

    public static GitHubRepositoryTarget ParseRepositoryTarget(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return InvalidTarget("請貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository 網址。");

        var ssh = GitSshRemoteRegex().Match(value);
        if (ssh.Success)
        {
            value = $"https://{ssh.Groups["host"].Value}/{ssh.Groups["path"].Value}";
        }
        else if (!value.Contains("://", StringComparison.Ordinal))
        {
            if (value.Contains(".github.io", StringComparison.OrdinalIgnoreCase))
                value = $"https://{value.TrimStart('/')}";
            else
                value = SupportedHosts.Any(host => value.StartsWith(host + "/", StringComparison.OrdinalIgnoreCase))
                    ? $"https://{value}"
                    : $"https://github.com/{value.TrimStart('/')}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return InvalidTarget(UnsupportedRepositoryUrlMessage);
        }

        var convertedPagesUrl = GitHubPagesUrl.TryConvertToRepositoryUrl(uri);
        if (convertedPagesUrl is not null)
            return ParseRepositoryTarget(convertedPagesUrl);

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return InvalidTarget(UnsupportedRepositoryUrlMessage);

        var platform = PlatformFromHost(uri.Host);
        if (platform is null)
            return InvalidTarget(UnsupportedRepositoryUrlMessage);

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (platform == GitHostingPlatform.Bitbucket
            && segments.Length >= 3
            && segments[2].Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            segments = segments[..2];
        }

        if (segments.Length < 2)
            return InvalidTarget("網址必須指向 repository 首頁，不可包含 issues、settings 等子路徑。");

        var owner = string.Join('/', segments[..^1].Select(Uri.UnescapeDataString));
        var repository = Uri.UnescapeDataString(segments[^1]);
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];

        if (!RepositoryOwnerRegex().IsMatch(owner) || !GitHubRepositoryRegex().IsMatch(repository))
            return InvalidTarget("Repository owner／namespace 或名稱格式無效。");

        var (pagesUrl, jekyllUrl, baseUrl, userSite) = GetSuggestedSiteUrls(platform.Value, owner, repository);
        return new GitHubRepositoryTarget
        {
            Platform = platform.Value,
            IsValid = true,
            Owner = owner,
            Repository = repository,
            CanonicalUrl = $"https://{uri.Host}/{owner}/{repository}.git",
            PagesUrl = pagesUrl,
            JekyllUrl = jekyllUrl,
            JekyllBaseUrl = baseUrl,
            IsUserOrOrganizationSite = userSite
        };
    }

    public static string RemoteNameFor(GitHostingPlatform platform) => platform switch
    {
        GitHostingPlatform.GitLab => "gitlab",
        GitHostingPlatform.Codeberg => "codeberg",
        GitHostingPlatform.Bitbucket => "bitbucket",
        _ => "origin"
    };

    public async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "git", "--version", timeoutMs: 10_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<bool> IsGhAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "gh", "--version", timeoutMs: 10_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<GitRemoteInfo> GetInfoAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        CancellationToken cancellationToken = default)
    {
        var remoteName = RemoteNameFor(platform);
        var data = new GitRemoteInfo { RemoteName = remoteName };

        var branch = await _processRunner.RunAsync(
            "git", "branch --show-current", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (branch.Success)
            data.Branch = branch.StdOut.Trim();

        var remote = await _processRunner.RunAsync(
            "git", $"remote get-url {remoteName}", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remote.Success)
        {
            data.RemoteUrl = remote.StdOut.Trim();
            var (owner, repo) = ParseRepositoryRemote(data.RemoteUrl);
            data.Owner = owner;
            data.Repo = repo;
        }

        var auth = await _processRunner.RunAsync(
            "gh", "auth status", projectPath, timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        data.GhAuthenticated = auth.Success
            || auth.CombinedOutput.Contains("Logged in", StringComparison.OrdinalIgnoreCase);

        var user = await _processRunner.RunAsync(
            "gh", "api user --jq .login", projectPath, timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (user.Success)
            data.GhUser = user.StdOut.Trim();

        return data;
    }

    public async Task<(bool HasAccess, string Message)> CheckPushAccessAsync(
        GitHubRepositoryTarget target,
        CancellationToken cancellationToken = default)
    {
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.Owner) || string.IsNullOrWhiteSpace(target.Repository))
            return (false, target.ErrorMessage);

        if (target.Platform != GitHostingPlatform.GitHub)
        {
            if (string.IsNullOrWhiteSpace(target.CanonicalUrl))
                return (false, "找不到可檢查的 repository 網址。");

            var access = await _processRunner.RunAsync(
                "git",
                GitHostingAccessChecks.LsRemoteHeadArguments(target),
                timeoutMs: 60_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return GitHostingAccessChecks.FromLsRemoteResult(target, access);
        }

        var result = await _processRunner.RunAsync(
            "gh",
            $"api repos/{target.Owner}/{target.Repository} --jq .permissions.push",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return (false,
                $"無法確認 repository 權限。請確認 gh 已登入，且 repository 存在或目前帳號可存取。\n{result.CombinedOutput}");
        }

        var canPush = result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        return canPush
            ? (true, $"已確認具有 {target.Owner}/{target.Repository} 的推送權限。")
            : (false, $"目前 GitHub 登入帳號沒有 {target.Owner}/{target.Repository} 的推送權限。請由 owner 加入 collaborator，或改用有權限的帳號執行 gh auth login。");
    }

    public async Task<GitHubRepositoryLookup> LookupOwnedRepositoryAsync(
        string repoName,
        CancellationToken cancellationToken = default)
    {
        var name = repoName.Trim();
        if (!GitHubRepositoryRegex().IsMatch(name))
            return GitHubRepositoryLookup.Fail("Repository 名稱格式無效。");

        var user = await _processRunner.RunAsync(
            "gh", "api user --jq .login", timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!user.Success || string.IsNullOrWhiteSpace(user.StdOut))
            return GitHubRepositoryLookup.Fail("無法取得目前 GitHub 帳號。請先執行 gh auth login。");

        var owner = user.StdOut.Trim();
        var target = ParseRepositoryTarget($"https://github.com/{owner}/{name}");
        if (!target.IsValid)
            return GitHubRepositoryLookup.Fail(target.ErrorMessage);

        var meta = await _processRunner.RunAsync(
            "gh",
            $"api repos/{owner}/{name}",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!meta.Success)
        {
            return IsNotFound(meta)
                ? GitHubRepositoryLookup.Missing()
                : GitHubRepositoryLookup.Fail(
                    $"無法確認 GitHub 上是否已有 {owner}/{name}。\n{meta.CombinedOutput}");
        }

        var contents = await _processRunner.RunAsync(
            "gh",
            $"api repos/{owner}/{name}/contents",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> names;
        if (!contents.Success)
        {
            if (IsEmptyRepository(contents) || IsNotFound(contents))
                names = [];
            else
                return GitHubRepositoryLookup.Fail(
                    $"無法讀取 {owner}/{name} 的檔案清單。\n{contents.CombinedOutput}");
        }
        else
        {
            names = ParseRepositoryContentNames(contents.StdOut);
        }

        var looksLikeJekyll = GitHubRepositoryClassifier.LooksLikeJekyll(names);
        var canReuse = GitHubRepositoryClassifier.CanReuseExisting(names);
        var message = looksLikeJekyll
            ? $"GitHub 上已有 Jekyll repository {owner}/{name}，改用安全連結流程（會保留遠端內容，衝突時停止，不會 force push）。"
            : canReuse
                ? $"GitHub 上已有 {owner}/{name}（空的或僅有 README 等初始檔），改用安全連結流程。"
                : $"GitHub 上已有同名 repository {owner}/{name}，但看起來不是 Jekyll 網站。請改用其他名稱，或到上方「連結既有 GitHub Repository」貼上網址確認後再連。";

        return new GitHubRepositoryLookup
        {
            CheckSucceeded = true,
            Exists = true,
            CanReuse = canReuse,
            LooksLikeJekyll = looksLikeJekyll,
            Target = target,
            Message = message
        };
    }

    public async Task<GitHubPagesSitesResult> ListPagesSitesAsync(CancellationToken cancellationToken = default)
    {
        var user = await _processRunner.RunAsync(
            "gh", "api user --jq .login", timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!user.Success || string.IsNullOrWhiteSpace(user.StdOut))
            return GitHubPagesSitesResult.Fail("無法取得目前 GitHub 帳號。請先執行 GitHub 登入 (gh auth login)。");

        var login = user.StdOut.Trim();
        var args = "api --paginate --slurp \"user/repos?per_page=100&sort=updated&affiliation=owner,collaborator\"";
        var result = await _processRunner.RunAsync(
            "gh", args, timeoutMs: 90_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success && ContainsAny(result.CombinedOutput, "unknown flag", "unknown command"))
        {
            result = await _processRunner.RunAsync(
                "gh",
                "api --paginate \"user/repos?per_page=100&sort=updated&affiliation=owner,collaborator\"",
                timeoutMs: 90_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (!result.Success)
        {
            return GitHubPagesSitesResult.Fail(
                $"無法列出 GitHub Pages 網站。請確認 gh 已登入。\n{result.CombinedOutput}");
        }

        var sites = GitHubPagesSiteParser.Parse(result.StdOut, login);
        return new GitHubPagesSitesResult
        {
            Success = true,
            Sites = sites,
            Message = sites.Count == 0
                ? "沒有找到已啟用 GitHub Pages 的 repository。也可以直接貼上 repository 或 Pages 網址複製。"
                : $"找到 {sites.Count} 個 GitHub Pages 網站。"
        };
    }

    public async Task<ProcessResult> CloneRepositoryAsync(
        GitHubRepositoryTarget target,
        string destinationPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.Owner) || string.IsNullOrWhiteSpace(target.Repository))
            return new ProcessResult { ExitCode = -1, StdErr = target.ErrorMessage };

        string dest;
        try
        {
            dest = Path.GetFullPath(destinationPath);
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = -1, StdErr = $"本機路徑無效：{ex.Message}" };
        }

        if (dest.Contains('"'))
            return new ProcessResult { ExitCode = -1, StdErr = "本機路徑不可包含引號。" };

        if (!GitHubCloneDestination.IsVacant(dest))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = $"目標資料夾不是空的：{dest}。請換一個資料夾名稱，以免覆蓋現有檔案。"
            };
        }

        var parent = Path.GetDirectoryName(dest);
        if (string.IsNullOrWhiteSpace(parent))
            return new ProcessResult { ExitCode = -1, StdErr = "本機路徑無效。" };

        Directory.CreateDirectory(parent);

        var ghOk = target.Platform == GitHostingPlatform.GitHub
            && await IsGhAvailableAsync(cancellationToken).ConfigureAwait(false);
        ProcessResult clone;
        if (ghOk)
        {
            progress?.Report($"正在複製 {target.Owner}/{target.Repository}…");
            clone = await _processRunner.RunAsync(
                "gh",
                $"repo clone \"{target.Owner}/{target.Repository}\" \"{dest}\"",
                parent,
                progress,
                cancellationToken,
                timeoutMs: 300_000).ConfigureAwait(false);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(target.CanonicalUrl))
                return new ProcessResult { ExitCode = -1, StdErr = "找不到可複製的 repository 網址。" };

            progress?.Report($"正在 git clone {target.Owner}/{target.Repository}…");
            clone = await _processRunner.RunAsync(
                "git",
                $"clone \"{target.CanonicalUrl}\" \"{dest}\"",
                parent,
                progress,
                cancellationToken,
                timeoutMs: 300_000).ConfigureAwait(false);
        }

        if (clone.Success)
        {
            return new ProcessResult
            {
                ExitCode = 0,
                StdOut = $"已複製 {target.Owner}/{target.Repository} 到 {dest}"
            };
        }

        TryDeleteIncompleteClone(dest);
        return clone;
    }

    public Task UpdateSiteUrlsAsync(
        string projectPath,
        GitHubRepositoryTarget target,
        CancellationToken cancellationToken = default)
    {
        var url = target.JekyllUrl ?? string.Empty;
        var baseUrl = target.JekyllBaseUrl ?? string.Empty;
        return _configService.UpdateUrlAndBaseUrlAsync(projectPath, url, baseUrl, cancellationToken);
    }

    public async Task<ProcessResult> InitRepositoryAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(projectPath, ".git")))
        {
            var init = await _processRunner.RunAsync(
                    "git", "init -b main", projectPath, output, cancellationToken, timeoutMs: 30_000)
                .ConfigureAwait(false);
            if (!init.Success)
                return init;
        }

        await EnsureGitignoreAsync(projectPath, cancellationToken).ConfigureAwait(false);
        return new ProcessResult { ExitCode = 0, StdOut = "Git repository ready." };
    }

    public async Task<string> EnsureGitHubActionsWorkflowAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(projectPath, ".github", "workflows");
        Directory.CreateDirectory(dir);
        var workflow = Path.Combine(dir, "jekyll.yml");

        if (File.Exists(workflow))
        {
            var existing = await File.ReadAllTextAsync(workflow, cancellationToken).ConfigureAwait(false);
            if (existing.Contains("jekyll-build-pages", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(workflow, GitHubPagesWorkflow, cancellationToken).ConfigureAwait(false);
                return "已將 .github/workflows/jekyll.yml 更新為 Bundler 建置流程。";
            }

            return "已有 .github/workflows/jekyll.yml";
        }

        var existingPages = await FindExistingPagesWorkflowAsync(dir, cancellationToken).ConfigureAwait(false);
        if (existingPages is not null)
            return $"已偵測到 Pages 工作流程：{existingPages}，未重複建立。";

        await File.WriteAllTextAsync(workflow, GitHubPagesWorkflow, cancellationToken).ConfigureAwait(false);
        return "已寫入 .github/workflows/jekyll.yml";
    }

    public async Task<ProcessResult> CreateRepoAndPushAsync(
        string projectPath,
        string repoName,
        bool isPrivate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("初始化 Git…");
        var init = await InitRepositoryAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (!init.Success) return init;

        progress?.Report("加入 GitHub Actions 工作流程…");
        var workflowMessage = await EnsureGitHubActionsWorkflowAsync(projectPath, cancellationToken).ConfigureAwait(false);
        progress?.Report(workflowMessage);

        var platformError = await EnsureGitHubActionsBundlePlatformsAsync(projectPath, progress, cancellationToken)
            .ConfigureAwait(false);
        if (platformError is not null) return platformError;

        var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;

        progress?.Report("提交檔案…");
        var initialCommit = await CommitAllAsync(projectPath, "Initial commit via Jekyller", progress, cancellationToken)
            .ConfigureAwait(false);
        if (!initialCommit.Success) return initialCommit;

        var visibility = isPrivate ? "--private" : "--public";
        progress?.Report($"建立 GitHub repository：{repoName}…");

        var remote = await _processRunner.RunAsync(
            "git", "remote get-url origin", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (remote.Success)
        {
            var (owner, repo) = ParseGitHubRemote(remote.StdOut.Trim());
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 origin 已指向 {remote.StdOut.Trim()}，不是 GitHub repository。Jekyller 沒有修改 origin，也沒有建立新 repository。"
                };
            }

            if (!repo.Equals(repoName, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 origin 已指向 {owner}/{repo}，與要建立的「{repoName}」不同。Jekyller 沒有修改 origin，也沒有建立新 repository。"
                };
            }

            progress?.Report("本機已有 origin，推送到既有 repository…");
            var push = await _processRunner.RunAsync(
                "git", "push -u origin HEAD", projectPath, progress, cancellationToken, timeoutMs: 180_000)
                .ConfigureAwait(false);
            if (!push.Success)
                return push;
        }
        else
        {
            var create = await _processRunner.RunAsync(
                "gh",
                $"repo create \"{repoName}\" {visibility} --source=. --remote=origin --push",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 180_000).ConfigureAwait(false);

            if (!create.Success)
            {
                if (!LooksLikeNameExistsError(create))
                    return create;

                var info = await GetInfoAsync(projectPath, GitHostingPlatform.GitHub, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(info.GhUser))
                    return create;

                var target = ParseRepositoryTarget($"https://github.com/{info.GhUser}/{repoName}");
                if (!target.IsValid)
                    return create;

                progress?.Report($"GitHub 上已有 {info.GhUser}/{repoName}，改為安全連結既有 repository…");
                return await ConnectExistingRepositoryAndPushAsync(
                    projectPath,
                    target,
                    "Publish site via Jekyller",
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        progress?.Report("啟用 GitHub Pages（GitHub Actions）…");
        var pages = await EnablePagesFromActionsAsync(
            projectPath,
            progress,
            cancellationToken,
            allowManualSetupIfPushCompleted: true).ConfigureAwait(false);
        if (pages.Success)
        {
            return new ProcessResult
            {
                ExitCode = 0,
                StdOut = $"Repository 已建立並推送完成。\n{pages.CombinedOutput}"
            };
        }

        return new ProcessResult
        {
            ExitCode = pages.ExitCode,
            StdOut = "Repository 已建立並推送完成，但 GitHub Pages 設定尚未完成。",
            StdErr = pages.StdErr
        };
    }

    public async Task<ProcessResult> ConnectExistingRepositoryAndPushAsync(
        string projectPath,
        GitHubRepositoryTarget target,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.CanonicalUrl))
            return new ProcessResult { ExitCode = -1, StdErr = target.ErrorMessage };

        progress?.Report("初始化本機 Git repository…");
        var init = await InitRepositoryAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (!init.Success) return init;

        var remoteName = RemoteNameFor(target.Platform);
        var remote = await _processRunner.RunAsync(
            "git", $"remote get-url {remoteName}", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remote.Success)
        {
            var existingTarget = ParseRepositoryTarget(remote.StdOut.Trim());
            if (!existingTarget.IsValid
                || existingTarget.Platform != target.Platform
                || !string.Equals(existingTarget.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existingTarget.Repository, target.Repository, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 {remoteName} remote 已指向其他 repository：{remote.StdOut.Trim()}。為避免推錯位置，Jekyller 未修改 {remoteName}。"
                };
            }
        }
        else
        {
            progress?.Report($"連結 {remoteName} remote：{target.Owner}/{target.Repository}…");
            var addRemote = await _processRunner.RunAsync(
                "git",
                $"remote add {remoteName} \"{target.CanonicalUrl}\"",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 15_000).ConfigureAwait(false);
            if (!addRemote.Success) return addRemote;
        }

        progress?.Report("抓取遠端預設分支…");
        var fetch = await _processRunner.RunAsync(
            "git", $"fetch {remoteName} --prune", projectPath, progress, cancellationToken, timeoutMs: 120_000)
            .ConfigureAwait(false);
        if (!fetch.Success)
            return GitHostingProcessErrors.WithRepositoryAccessHint(
                target.Platform,
                "抓取遠端預設分支",
                fetch);

        var remoteHead = await _processRunner.RunAsync(
            "git", $"ls-remote --symref {remoteName} HEAD", projectPath, timeoutMs: 30_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var branchMatch = RemoteHeadRegex().Match(remoteHead.StdOut);
        var remoteBranch = branchMatch.Success ? branchMatch.Groups["branch"].Value : "main";
        if (!GitBranchRegex().IsMatch(remoteBranch))
            return new ProcessResult { ExitCode = -1, StdErr = "遠端預設分支名稱格式不安全，已停止操作。" };
        var remoteRefCheck = await _processRunner.RunAsync(
            "git", $"rev-parse --verify \"refs/remotes/{remoteName}/{remoteBranch}\"", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var remoteHasCommit = remoteRefCheck.Success || RemoteHeadCommitRegex().IsMatch(remoteHead.StdOut);

        var localHead = await _processRunner.RunAsync(
            "git", "rev-parse --verify HEAD", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remoteHasCommit && !localHead.Success)
        {
            progress?.Report($"以遠端 {remoteBranch} 為基準，保留本機未追蹤網站檔案…");
            var checkout = await _processRunner.RunAsync(
                "git",
                $"checkout -B \"{remoteBranch}\" --track \"{remoteName}/{remoteBranch}\"",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 30_000).ConfigureAwait(false);
            if (!checkout.Success)
            {
                return new ProcessResult
                {
                    ExitCode = checkout.ExitCode,
                    StdErr = $"遠端檔案與本機未追蹤檔案衝突，已停止連結；沒有強制覆蓋。\n{checkout.CombinedOutput}"
                };
            }
        }
        else if (remoteHasCommit && localHead.Success)
        {
            await CommitAllAsync(projectPath, "Pre-sync local site files", progress, cancellationToken).ConfigureAwait(false);
            progress?.Report($"合併遠端 {remoteBranch}（允許初始 README 歷史）…");
            var merge = await _processRunner.RunAsync(
                "git",
                $"merge \"{remoteName}/{remoteBranch}\" --allow-unrelated-histories --no-edit",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 60_000).ConfigureAwait(false);
            if (!merge.Success)
            {
                await _processRunner.RunAsync(
                    "git", "merge --abort", projectPath, timeoutMs: 15_000, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return new ProcessResult
                {
                    ExitCode = merge.ExitCode,
                    StdErr = $"遠端內容與本機內容發生合併衝突，已中止合併且未推送。\n{merge.CombinedOutput}"
                };
            }
        }

        if (target.Platform == GitHostingPlatform.GitHub)
        {
            progress?.Report("加入 GitHub Actions workflow 並提交網站…");
            var workflowMessage = await EnsureGitHubActionsWorkflowAsync(projectPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(workflowMessage);
            var platformError = await EnsureGitHubActionsBundlePlatformsAsync(projectPath, progress, cancellationToken)
                .ConfigureAwait(false);
            if (platformError is not null) return platformError;
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;
        }
        else if (target.Platform == GitHostingPlatform.GitLab)
        {
            progress?.Report("加入 GitLab Pages CI 並固定相容的 Hugo 版本…");
            await EnsureGitLabPagesCiAsync(projectPath, cancellationToken).ConfigureAwait(false);
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;
        }
        else
        {
            progress?.Report($"提交網站到 {target.PlatformLabel}（不加入 GitHub Actions workflow）…");
            await EnsureHostingNotesAsync(projectPath, target.Platform, cancellationToken).ConfigureAwait(false);
        }
        string? staticOutputDirectory = null;
        if (StaticPagesDeployment.ShouldPublishOutputBranch(target))
        {
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;

            if (!StaticPagesDeployment.TryFindOutputDirectory(projectPath, out staticOutputDirectory, out var outputError))
                return new ProcessResult { ExitCode = -1, StdErr = outputError };

            CopyDeploymentMarkerToOutput(projectPath, staticOutputDirectory);
        }
        var commit = await CommitAllAsync(projectPath, commitMessage, progress, cancellationToken).ConfigureAwait(false);
        if (!commit.Success) return commit;

        if (target.Platform != GitHostingPlatform.GitHub
            && StaticPagesDeployment.ShouldPushSourceBranch(target))
        {
            progress?.Report($"確認 {target.PlatformLabel} 推送權限…");
            var dryRun = await _processRunner.RunAsync(
                "git",
                GitHostingAccessChecks.PushDryRunArguments(remoteBranch, remoteName),
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 60_000).ConfigureAwait(false);
            if (!dryRun.Success)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Platform,
                    "推送",
                    dryRun);
        }

        if (StaticPagesDeployment.ShouldPushSourceBranch(target))
        {
            progress?.Report($"推送到 {target.Owner}/{target.Repository}…");
            var push = await _processRunner.RunAsync(
                "git",
                $"push -u {remoteName} HEAD:\"{remoteBranch}\"",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 180_000).ConfigureAwait(false);
            if (!push.Success)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Platform,
                    "推送",
                    push);
        }

        if (StaticPagesDeployment.ShouldPublishOutputBranch(target))
        {
            var outputBranch = StaticPagesDeployment.OutputBranchFor(target.Platform)!;
            var deploy = await PublishStaticOutputBranchAsync(
                staticOutputDirectory!,
                target,
                outputBranch,
                commitMessage,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!deploy.Success) return deploy;
        }

        if (target.Platform == GitHostingPlatform.GitHub)
        {
            progress?.Report("啟用 GitHub Pages（Actions）…");
            return await EnablePagesFromActionsAsync(
                projectPath,
                progress,
                cancellationToken,
                allowManualSetupIfPushCompleted: true).ConfigureAwait(false);
        }

        return new ProcessResult
        {
            ExitCode = 0,
            StdOut = $"已安全連結並推送到 {target.PlatformLabel}。靜態網站部署請依該平台的 CI／Pages 設定啟用。"
        };
    }

    public async Task<ProcessResult> PushAsync(
        string projectPath,
        string commitMessage,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("提交變更…");
        var remoteName = RemoteNameFor(platform);
        var remoteUrl = await DetectRemoteAsync(projectPath, platform, cancellationToken).ConfigureAwait(false);
        var remoteTarget = ParseRepositoryTarget(remoteUrl);
        if (remoteTarget.IsValid && remoteTarget.Platform == GitHostingPlatform.GitHub)
        {
            var workflowMessage = await EnsureGitHubActionsWorkflowAsync(projectPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(workflowMessage);
            var platformError = await EnsureGitHubActionsBundlePlatformsAsync(projectPath, progress, cancellationToken)
                .ConfigureAwait(false);
            if (platformError is not null) return platformError;
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;
        }
        else if (remoteTarget.IsValid && remoteTarget.Platform == GitHostingPlatform.GitLab)
        {
            progress?.Report("更新 GitLab Pages CI 並固定相容的 Hugo 版本…");
            await EnsureGitLabPagesCiAsync(projectPath, cancellationToken).ConfigureAwait(false);
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;
        }
        else if (remoteTarget.IsValid)
        {
            await EnsureHostingNotesAsync(projectPath, remoteTarget.Platform, cancellationToken).ConfigureAwait(false);
        }
        string? staticOutputDirectory = null;
        if (remoteTarget.IsValid && StaticPagesDeployment.ShouldPublishOutputBranch(remoteTarget))
        {
            var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
            if (markerError is not null) return markerError;

            if (!StaticPagesDeployment.TryFindOutputDirectory(projectPath, out staticOutputDirectory, out var outputError))
                return new ProcessResult { ExitCode = -1, StdErr = outputError };

            CopyDeploymentMarkerToOutput(projectPath, staticOutputDirectory);
        }
        var commit = await CommitAllAsync(projectPath, commitMessage, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(commit.CombinedOutput);
        if (!commit.Success) return commit;

        var branch = await GetCurrentOrRemoteDefaultBranchAsync(projectPath, remoteName, cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "無法判斷要推送的 Git branch，已停止推送。"
            };
        }

        if (remoteTarget.IsValid
            && remoteTarget.Platform != GitHostingPlatform.GitHub
            && StaticPagesDeployment.ShouldPushSourceBranch(remoteTarget))
        {
            progress?.Report($"確認 {remoteTarget.PlatformLabel} 推送權限…");
            var dryRun = await _processRunner.RunAsync(
                "git",
                GitHostingAccessChecks.PushDryRunArguments(branch, remoteName),
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 60_000).ConfigureAwait(false);
            if (!dryRun.Success)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    remoteTarget.Platform,
                    "推送",
                    dryRun);
        }

        var sourcePush = new ProcessResult { ExitCode = 0 };
        if (!remoteTarget.IsValid || StaticPagesDeployment.ShouldPushSourceBranch(remoteTarget))
        {
            progress?.Report($"git push -u {remoteName} HEAD:{branch}…");
            var push = await _processRunner.RunAsync(
                "git", $"push -u {remoteName} HEAD:\"{branch}\"", projectPath, progress, cancellationToken, timeoutMs: 180_000)
                .ConfigureAwait(false);
            sourcePush = GitHostingProcessErrors.WithRepositoryAccessHint(remoteTarget.Platform, "推送", push);
            if (!sourcePush.Success) return sourcePush;
        }

        if (remoteTarget.IsValid && StaticPagesDeployment.ShouldPublishOutputBranch(remoteTarget))
        {
            var outputBranch = StaticPagesDeployment.OutputBranchFor(remoteTarget.Platform)!;
            return await PublishStaticOutputBranchAsync(
                staticOutputDirectory!,
                remoteTarget,
                outputBranch,
                commitMessage,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        return sourcePush;
    }

    public async Task<ProcessResult> PullAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var remoteName = RemoteNameFor(platform);
        var branch = await GetCurrentOrRemoteDefaultBranchAsync(projectPath, remoteName, cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "無法判斷要拉取的 Git branch，已停止 pull。"
            };
        }

        output?.Report($"從 {remoteName} 抓取最新變更…");
        var fetch = await _processRunner.RunAsync(
            "git", $"fetch {remoteName} --prune", projectPath, output, cancellationToken, timeoutMs: 120_000)
            .ConfigureAwait(false);
        if (!fetch.Success)
        {
            var remoteUrl = await DetectRemoteAsync(projectPath, platform, cancellationToken).ConfigureAwait(false);
            var remoteTarget = ParseRepositoryTarget(remoteUrl);
            return remoteTarget.IsValid
                ? GitHostingProcessErrors.WithRepositoryAccessHint(
                    remoteTarget.Platform,
                    "拉取",
                    fetch)
                : fetch;
        }

        output?.Report($"合併 {remoteName}/{branch}（fast-forward only）…");
        return await _processRunner.RunAsync(
            "git", $"merge --ff-only \"{remoteName}/{branch}\"", projectPath, output, cancellationToken, timeoutMs: 120_000)
            .ConfigureAwait(false);
    }

    public async Task<string> GetLatestDeploymentAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
            return "尚未設定 GitHub remote。";

        var (owner, repo) = ownerRepo.Value;
        var result = await _processRunner.RunAsync(
            "gh",
            $"run list --repo \"{owner}/{repo}\" --limit 1 --json status,conclusion,displayTitle,url,createdAt,workflowName",
            projectPath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return "尚無部署紀錄，或 GitHub Actions 尚未開始執行。";

        try
        {
            using var document = JsonDocument.Parse(result.StdOut);
            var run = document.RootElement.EnumerateArray().FirstOrDefault();
            if (run.ValueKind == JsonValueKind.Undefined)
                return "尚無部署紀錄。";

            var status = run.GetProperty("status").GetString() ?? "unknown";
            var conclusion = run.TryGetProperty("conclusion", out var c) && c.ValueKind != JsonValueKind.Null
                ? c.GetString() ?? ""
                : "";
            var title = run.TryGetProperty("displayTitle", out var t)
                ? t.GetString() ?? "Deploy Jekyll to GitHub Pages"
                : "Deploy Jekyll to GitHub Pages";
            var workflow = run.TryGetProperty("workflowName", out var w) ? w.GetString() : null;
            var url = run.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            var label = string.IsNullOrWhiteSpace(workflow) ? title : $"{workflow} · {title}";
            return $"{label}\n狀態：{status}{(string.IsNullOrWhiteSpace(conclusion) ? "" : $" ({conclusion})")}\n{url}";
        }
        catch (JsonException)
        {
            return "無法解析 GitHub Actions 部署狀態。";
        }
    }

    public async Task<GitHubPagesStatus> GetPagesStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
        {
            return new GitHubPagesStatus
            {
                Success = false,
                Message = "找不到 GitHub remote（origin）。請先建立或連結 repository。"
            };
        }

        var (owner, repo) = ownerRepo.Value;
        var result = await _processRunner.RunAsync(
            "gh",
            $"api repos/{owner}/{repo}/pages",
            projectPath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var err = result.CombinedOutput;
            if (err.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
            {
                return new GitHubPagesStatus
                {
                    Success = true,
                    IsEnabled = false,
                    Status = "disabled",
                    Message = $"Repository {owner}/{repo} 尚未啟用 GitHub Pages。"
                };
            }

            return new GitHubPagesStatus
            {
                Success = false,
                Message = err
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
            var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty;
            var cname = root.TryGetProperty("cname", out var c) && c.ValueKind != JsonValueKind.Null
                ? c.GetString() ?? string.Empty
                : string.Empty;
            var buildType = root.TryGetProperty("build_type", out var b) ? b.GetString() ?? string.Empty : string.Empty;

            var branch = string.Empty;
            var path = string.Empty;
            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                branch = source.TryGetProperty("branch", out var br) ? br.GetString() ?? string.Empty : string.Empty;
                path = source.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            }

            return new GitHubPagesStatus
            {
                Success = true,
                IsEnabled = true,
                Status = status,
                HtmlUrl = htmlUrl,
                SourceBranch = branch,
                SourcePath = path,
                BuildType = buildType,
                Cname = cname,
                Message = status switch
                {
                    "built" => "GitHub Pages 已啟用，最近一次 Pages 狀態為 built；是否為本次最新內容請看線上版本監控或 Actions。",
                    "building" => "GitHub Pages 正在建置中；尚不能判定已上線。",
                    "errored" => "建置發生錯誤，請檢查 Actions 日誌。",
                    _ => $"GitHub Pages 狀態：{status}"
                }
            };
        }
        catch (Exception ex)
        {
            return new GitHubPagesStatus
            {
                Success = false,
                Message = $"無法解析 Pages 回應：{ex.Message}\n{result.StdOut}"
            };
        }
    }

    public async Task<ProcessResult> EnablePagesFromActionsAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        bool allowManualSetupIfPushCompleted = false)
    {
        var info = await GetInfoAsync(projectPath, GitHostingPlatform.GitHub, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "找不到 GitHub remote（origin）。請先建立或連結 repository。"
            };
        }

        output?.Report("檢查目前 GitHub Pages 設定…");
        var current = await _processRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo}/pages",
            projectPath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var pagesExist = current.Success;
        if (pagesExist)
        {
            try
            {
                using var doc = JsonDocument.Parse(current.StdOut);
                var buildType = doc.RootElement.TryGetProperty("build_type", out var buildTypeProperty)
                    ? buildTypeProperty.GetString() ?? string.Empty
                    : string.Empty;
                if (buildType.Equals("workflow", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessResult
                    {
                        ExitCode = 0,
                        StdOut = "GitHub Pages 已設定為 GitHub Actions，無需管理權限再次修改。"
                    };
                }
            }
            catch
            {
                // Fall through to the normal permission/update path so GitHub can report a clear error.
            }
        }
        else if (!current.CombinedOutput.Contains("404", StringComparison.OrdinalIgnoreCase)
                 && !current.CombinedOutput.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        output?.Report("確認 GitHub Pages 管理權限…");
        var permission = await _processRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo} --jq .permissions.admin",
            projectPath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!permission.Success)
        {
            return new ProcessResult
            {
                ExitCode = permission.ExitCode,
                StdErr = $"無法確認 GitHub Pages 管理權限。請確認 gh 已登入且 repository 可存取。\n{permission.CombinedOutput}"
            };
        }

        var canManagePages = permission.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!canManagePages)
        {
            return new ProcessResult
            {
                ExitCode = allowManualSetupIfPushCompleted ? 0 : -1,
                StdOut = allowManualSetupIfPushCompleted
                    ? $"網站檔案與 GitHub Actions workflow 已成功推送到 {info.Owner}/{info.Repo}。\n" +
                      "目前登入帳號沒有管理 GitHub Pages 設定所需的 admin 權限，因此無法自動切換 Pages Source。\n" +
                      "請 Repository 擁有者開啟 Settings > Pages，在 Build and deployment 的 Source 選擇 GitHub Actions；完成後回到 Jekyller 按「查詢 Pages 狀態」。"
                    : "網站檔案與 GitHub Actions workflow 已成功推送。",
                StdErr = allowManualSetupIfPushCompleted
                    ? string.Empty
                    : $"目前登入帳號具有 {info.Owner}/{info.Repo} 的推送權限，但沒有管理 GitHub Pages 設定所需的 admin 權限。\n" +
                      "請 Repository 擁有者開啟 Settings > Pages，在 Build and deployment 的 Source 選擇 GitHub Actions；完成後回到 Jekyller 按「查詢 Pages 狀態」。"
            };
        }

        var method = pagesExist ? "PUT" : "POST";
        var update = await _processRunner.RunAsync(
            "gh",
            $"api -X {method} repos/{info.Owner}/{info.Repo}/pages -f build_type=workflow",
            projectPath,
            output,
            cancellationToken,
            timeoutMs: 60_000).ConfigureAwait(false);

        if (update.Success)
        {
            return new ProcessResult
            {
                ExitCode = 0,
                StdOut = pagesExist
                    ? "已將 GitHub Pages 建置來源更新為 GitHub Actions。"
                    : "已啟用 GitHub Pages（GitHub Actions）。"
            };
        }

        return new ProcessResult
        {
            ExitCode = update.ExitCode,
            StdErr = $"無法將 GitHub Pages 設為 GitHub Actions。\n{update.CombinedOutput}"
        };
    }

    public async Task<string?> DetectRemoteAsync(
        string projectPath,
        GitHostingPlatform platform = GitHostingPlatform.GitHub,
        CancellationToken cancellationToken = default)
    {
        var remoteName = RemoteNameFor(platform);
        var result = await _processRunner.RunAsync(
            "git",
            $"remote get-url {remoteName}",
            projectPath,
            timeoutMs: 10_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<(string Owner, string Repo)?> ParseOwnerRepoAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var remote = await DetectRemoteAsync(projectPath, GitHostingPlatform.GitHub, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(remote))
            return null;

        var (owner, repo) = ParseGitHubRemote(remote);
        return owner is null || repo is null ? null : (owner, repo);
    }

    public Task<ProcessResult> OpenGhAuthLoginAsync(CancellationToken cancellationToken = default)
        => _processRunner.RunAsync(
            "gh",
            "auth login --web --git-protocol https",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken);

    private async Task<ProcessResult> CommitAllAsync(
        string projectPath,
        string message,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await _processRunner.RunAsync(
            "git", "add -A", projectPath, progress, cancellationToken, timeoutMs: 60_000).ConfigureAwait(false);
        var status = await _processRunner.RunAsync(
            "git", "status --porcelain", projectPath, timeoutMs: 30_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.StdOut))
            return new ProcessResult { ExitCode = 0, StdOut = "沒有需要提交的變更。" };

        Dictionary<string, string?>? env = null;
        var email = await _processRunner.RunAsync(
            "git", "config user.email", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!email.Success || string.IsNullOrWhiteSpace(email.StdOut))
        {
            env = new Dictionary<string, string?>
            {
                ["GIT_AUTHOR_NAME"] = "Jekyller",
                ["GIT_AUTHOR_EMAIL"] = "jekyller@local",
                ["GIT_COMMITTER_NAME"] = "Jekyller",
                ["GIT_COMMITTER_EMAIL"] = "jekyller@local"
            };
        }

        var msg = message.Replace("\"", "'");
        return await _processRunner.RunAsync(
            "git",
            $"commit -m \"{msg}\"",
            projectPath,
            progress,
            cancellationToken,
            env,
            60_000).ConfigureAwait(false);
    }

    private async Task<ProcessResult?> PrepareDeploymentMarkerAsync(
        string sitePath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var marker = await _deploymentMonitor.PrepareDeploymentAsync(sitePath, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report($"建立部署版本標記：{marker.DeploymentId}");
            return null;
        }
        catch (Exception ex)
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = $"無法建立部署版本標記，已停止提交與推送：{ex.Message}"
            };
        }
    }

    private async Task<ProcessResult> PublishStaticOutputBranchAsync(
        string outputDirectory,
        GitHubRepositoryTarget target,
        string branch,
        string commitMessage,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.CanonicalUrl))
            return new ProcessResult { ExitCode = -1, StdErr = "找不到可推送的 Codeberg repository 網址。" };

        var tempRoot = Path.Combine(Path.GetTempPath(), "JekyllerStaticPages", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            progress?.Report($"準備 {target.PlatformLabel} Pages 輸出 branch：{branch}…");

            var init = await _processRunner.RunAsync(
                "git",
                "init",
                tempRoot,
                progress,
                cancellationToken,
                timeoutMs: 30_000).ConfigureAwait(false);
            if (!init.Success) return init;

            var remote = await _processRunner.RunAsync(
                "git",
                $"remote add origin \"{target.CanonicalUrl}\"",
                tempRoot,
                progress,
                cancellationToken,
                timeoutMs: 30_000).ConfigureAwait(false);
            if (!remote.Success) return remote;

            var fetch = await _processRunner.RunAsync(
                "git",
                $"fetch origin \"{branch}\"",
                tempRoot,
                progress,
                cancellationToken,
                timeoutMs: 120_000).ConfigureAwait(false);

            if (fetch.Success)
            {
                var checkout = await _processRunner.RunAsync(
                    "git",
                    $"checkout -B \"{branch}\" \"origin/{branch}\"",
                    tempRoot,
                    progress,
                    cancellationToken,
                    timeoutMs: 30_000).ConfigureAwait(false);
                if (!checkout.Success) return checkout;
                ClearDirectoryExceptGit(tempRoot);
            }
            else
            {
                var checkout = await _processRunner.RunAsync(
                    "git",
                    $"checkout --orphan \"{branch}\"",
                    tempRoot,
                    progress,
                    cancellationToken,
                    timeoutMs: 30_000).ConfigureAwait(false);
                if (!checkout.Success) return checkout;
            }

            CopyDirectoryContents(outputDirectory, tempRoot);

            var commit = await CommitAllAsync(
                tempRoot,
                string.IsNullOrWhiteSpace(commitMessage) ? "Deploy static site via Jekyller" : commitMessage.Trim(),
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!commit.Success) return commit;

            progress?.Report($"確認 {target.PlatformLabel} Pages branch 推送權限…");
            var dryRun = await _processRunner.RunAsync(
                "git",
                GitHostingAccessChecks.PushDryRunArguments(branch),
                tempRoot,
                progress,
                cancellationToken,
                timeoutMs: 60_000).ConfigureAwait(false);
            if (!dryRun.Success)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Platform,
                    "推送 Pages branch",
                    dryRun);

            progress?.Report($"推送 {target.PlatformLabel} Pages 輸出到 {branch} branch…");
            var push = await _processRunner.RunAsync(
                "git",
                $"push -u origin HEAD:\"{branch}\"",
                tempRoot,
                progress,
                cancellationToken,
                timeoutMs: 180_000).ConfigureAwait(false);
            if (!push.Success)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Platform,
                    "推送 Pages branch",
                    push);

            return new ProcessResult
            {
                ExitCode = 0,
                StdOut = target.Platform == GitHostingPlatform.Codeberg
                    ? "已將 Jekyll 靜態輸出推送到 Codeberg Pages 的 pages 分支。\n" +
                      "若尚未設定 Webhook，請在 Codeberg repository Settings > Webhooks 新增 Forgejo webhook，Target URL 使用 Pages 網址，Branch filter 設為 pages。"
                    : $"已推送 {target.PlatformLabel} 靜態輸出到 {branch} branch。"
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void CopyDeploymentMarkerToOutput(string projectPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var fileName in DeploymentMarkerFiles.OutputFileNames)
        {
            var source = Path.Combine(projectPath, fileName);
            if (!File.Exists(source)) continue;
            File.Copy(source, Path.Combine(outputDirectory, fileName), overwrite: true);
        }
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            if (IsGitPath(relative)) continue;
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (IsGitPath(relative)) continue;

            var destination = Path.Combine(destinationDirectory, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void ClearDirectoryExceptGit(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for the temporary deployment clone.
        }
    }

    private static bool IsGitPath(string relativePath) =>
        relativePath.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith(".git" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static async Task EnsureGitLabPagesCiAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
                GitLabPagesCi.PathFor(projectPath),
                GitLabPagesCi.Configuration,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureHostingNotesAsync(
        string projectPath,
        GitHostingPlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform is not (GitHostingPlatform.Codeberg or GitHostingPlatform.Bitbucket))
            return;

        var docsDir = Path.Combine(projectPath, "docs");
        Directory.CreateDirectory(docsDir);
        var fileName = platform == GitHostingPlatform.Codeberg
            ? "codeberg-pages.md"
            : "bitbucket-pages.md";
        var content = platform == GitHostingPlatform.Codeberg
            ? CodebergPagesNotes
            : BitbucketPagesNotes;
        await File.WriteAllTextAsync(
                Path.Combine(docsDir, fileName),
                content,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> FindExistingPagesWorkflowAsync(
        string workflowsDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workflowsDir))
            return null;

        var files = Directory.EnumerateFiles(workflowsDir, "*.yml")
            .Concat(Directory.EnumerateFiles(workflowsDir, "*.yaml"));

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            if (text.Contains("actions/deploy-pages", StringComparison.OrdinalIgnoreCase)
                || text.Contains("peaceiris/actions-gh-pages", StringComparison.OrdinalIgnoreCase)
                || text.Contains("jekyll-build-pages", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(file);
            }
        }

        return null;
    }

    private static async Task EnsureGitignoreAsync(string projectPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectPath, ".gitignore");
        const string defaults = """
                                _site/
                                .sass-cache/
                                .jekyll-cache/
                                .jekyll-metadata
                                vendor/
                                .bundle/
                                """;

        string existing;
        if (!File.Exists(path))
        {
            existing = string.Empty;
        }
        else
        {
            existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            existing = GemfileLockIgnoreRegex().Replace(existing, string.Empty);
        }

        var lines = defaults.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var missing = lines.Where(l => !existing.Contains(l, StringComparison.OrdinalIgnoreCase)).ToList();
        var updated = existing;
        if (missing.Count > 0)
        {
            if (updated.Length > 0 && !updated.EndsWith('\n'))
                updated += Environment.NewLine;
            updated += string.Join(Environment.NewLine, missing) + Environment.NewLine;
        }

        if (!string.Equals(existing, updated, StringComparison.Ordinal))
            await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessResult?> EnsureGitHubActionsBundlePlatformsAsync(
        string projectPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var lockfile = Path.Combine(projectPath, "Gemfile.lock");
        if (!File.Exists(lockfile))
            return null;

        progress?.Report("確認 Gemfile.lock 支援 GitHub Actions Linux 平台…");
        var result = await _processRunner.RunAsync(
            "bundle",
            "lock --add-platform x86_64-linux",
            projectPath,
            progress,
            cancellationToken,
            timeoutMs: 120_000).ConfigureAwait(false);

        if (result.Success)
            return null;

        return new ProcessResult
        {
            ExitCode = result.ExitCode,
            StdErr = "無法更新 Gemfile.lock 以支援 GitHub Actions Linux 平台；已停止提交與推送。\n" +
                     result.CombinedOutput
        };
    }

    private async Task<string?> GetCurrentOrRemoteDefaultBranchAsync(
        string projectPath,
        string remoteName,
        CancellationToken cancellationToken)
    {
        var current = await _processRunner.RunAsync(
            "git", "branch --show-current", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var branch = current.Success ? current.StdOut.Trim() : string.Empty;
        if (!string.IsNullOrWhiteSpace(branch))
            return GitBranchRegex().IsMatch(branch) ? branch : null;

        var remoteDefault = await GetRemoteDefaultBranchAsync(projectPath, remoteName, cancellationToken).ConfigureAwait(false);
        return remoteDefault is not null && GitBranchRegex().IsMatch(remoteDefault)
            ? remoteDefault
            : null;
    }

    private async Task<string?> GetRemoteDefaultBranchAsync(
        string projectPath,
        string remoteName,
        CancellationToken cancellationToken)
    {
        var remoteHead = await _processRunner.RunAsync(
            "git", $"ls-remote --symref {remoteName} HEAD", projectPath, timeoutMs: 30_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!remoteHead.Success)
            return "main";

        var branchMatch = RemoteHeadRegex().Match(remoteHead.StdOut);
        return branchMatch.Success ? branchMatch.Groups["branch"].Value : "main";
    }

    private static (string? Owner, string? Repo) ParseGitHubRemote(string url)
    {
        var m = GitHubRemoteRegex().Match(url);
        return m.Success ? (m.Groups["owner"].Value, m.Groups["repo"].Value) : (null, null);
    }

    private static (string? Owner, string? Repo) ParseRepositoryRemote(string url)
    {
        var target = ParseRepositoryTarget(url);
        return target.IsValid ? (target.Owner, target.Repository) : (null, null);
    }

    private static GitHostingPlatform? PlatformFromHost(string host) => host.ToLowerInvariant() switch
    {
        "github.com" => GitHostingPlatform.GitHub,
        "gitlab.com" => GitHostingPlatform.GitLab,
        "codeberg.org" => GitHostingPlatform.Codeberg,
        "bitbucket.org" => GitHostingPlatform.Bitbucket,
        _ => null
    };

    private static (string PagesUrl, string JekyllUrl, string BaseUrl, bool IsUserSite) GetSuggestedSiteUrls(
        GitHostingPlatform platform,
        string owner,
        string repository)
    {
        var account = owner.Split('/')[0].ToLowerInvariant();
        return platform switch
        {
            GitHostingPlatform.GitHub => BuildSiteUrls(account, repository, ".github.io"),
            GitHostingPlatform.GitLab => BuildSiteUrls(account, repository, ".gitlab.io"),
            GitHostingPlatform.Codeberg => BuildSiteUrls(account, repository, ".codeberg.page", "pages"),
            GitHostingPlatform.Bitbucket => BuildSiteUrls(account, repository, ".bitbucket.io"),
            _ => (string.Empty, string.Empty, string.Empty, false)
        };
    }

    private static (string PagesUrl, string JekyllUrl, string BaseUrl, bool IsUserSite) BuildSiteUrls(
        string account,
        string repository,
        string hostSuffix,
        string? userRepositoryName = null)
    {
        var expectedUserRepo = userRepositoryName ?? $"{account}{hostSuffix}";
        var userSite = repository.Equals(expectedUserRepo, StringComparison.OrdinalIgnoreCase);
        var host = $"https://{account}{hostSuffix}";
        return (userSite ? $"{host}/" : $"{host}/{repository}/", host, userSite ? string.Empty : $"/{repository}", userSite);
    }

    internal static List<string> ParseRepositoryContentNames(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var names = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        names.Add(value);
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                {
                    var value = name.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        names.Add(value);
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsNotFound(ProcessResult result) =>
        ContainsAny(result.CombinedOutput, "404", "Not Found");

    private static bool IsEmptyRepository(ProcessResult result) =>
        ContainsAny(result.CombinedOutput, "This repository is empty", "Git Repository is empty");

    private static bool LooksLikeNameExistsError(ProcessResult result) =>
        ContainsAny(
            result.CombinedOutput,
            "Name already exists on this account",
            "name already exists on this account",
            "already exists on this account");

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static void TryDeleteIncompleteClone(string destinationPath)
    {
        try
        {
            if (Directory.Exists(destinationPath) && GitHubCloneDestination.IsVacant(destinationPath))
                Directory.Delete(destinationPath, false);
        }
        catch
        {
            // Leave a partial folder for the user to inspect if cleanup fails.
        }
    }

    private static GitHubRepositoryTarget InvalidTarget(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };

    private const string UnsupportedRepositoryUrlMessage =
        "支援 GitHub、GitLab、Codeberg、Bitbucket repository 網址，以及 GitHub Pages 網址。";

    private static readonly string[] SupportedHosts = ["github.com", "gitlab.com", "codeberg.org", "bitbucket.org"];

    private const string CodebergPagesNotes = """
# Codeberg Pages deployment notes

Jekyller can clone and push this repository to Codeberg. When you deploy, Jekyller builds the Jekyll site and pushes the generated static files from `_site/` to the `pages` branch.

Codeberg Pages still needs one platform-side setup:

1. User / organization site: use a repository named `pages`, publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/`.
2. Repository site: publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/<repository>/`.
3. After the webhook or Forgejo Actions workflow is configured on Codeberg, push updates from Jekyller again.

Jekyller keeps Jekyll source in the normal local Git branch and publishes build output to the `pages` branch.
""";

    private const string BitbucketPagesNotes = """
# Bitbucket static website deployment notes

Jekyller can clone and push this repository to Bitbucket. Bitbucket Cloud static websites are workspace-level sites:

1. The repository that serves the website must be named `<workspace>.bitbucket.io`.
2. The live URL is `https://<workspace>.bitbucket.io/`.
3. Jekyller builds the Jekyll site and publishes generated static output.

Bitbucket does not provide GitHub-style per-project Pages URLs.
""";

    private const string GitHubPagesWorkflow = """
        name: Deploy Jekyll to GitHub Pages

        on:
          push:
            branches: ["main", "master"]
          workflow_dispatch:

        permissions:
          contents: read
          pages: write
          id-token: write

        concurrency:
          group: "pages"
          cancel-in-progress: false

        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - name: Checkout
                uses: actions/checkout@v4
                with:
                  fetch-depth: 0

              - name: Setup Pages
                id: pages
                uses: actions/configure-pages@v5

              - name: Setup Ruby
                uses: ruby/setup-ruby@v1
                with:
                  ruby-version: "3.3"
                  bundler-cache: true

              - name: Build with Jekyll
                run: bundle exec jekyll b -d "_site${{ steps.pages.outputs.base_path }}"
                env:
                  JEKYLL_ENV: production
                  TZ: Asia/Taipei

              - name: Upload artifact
                uses: actions/upload-pages-artifact@v3
                with:
                  path: "_site${{ steps.pages.outputs.base_path }}"

          deploy:
            environment:
              name: github-pages
              url: ${{ steps.deployment.outputs.page_url }}
            runs-on: ubuntu-latest
            needs: build
            steps:
              - name: Deploy to GitHub Pages
                id: deployment
                uses: actions/deploy-pages@v4
        """;

    [GeneratedRegex(@"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRemoteRegex();

    [GeneratedRegex(@"^git@(?<host>github\.com|gitlab\.com|codeberg\.org|bitbucket\.org):(?<path>[^\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitSshRemoteRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex GitHubOwnerRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]|/(?!/)){0,199}$")]
    private static partial Regex RepositoryOwnerRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepositoryRegex();

    [GeneratedRegex(@"ref:\s+refs/heads/(?<branch>[^\s]+)\s+HEAD")]
    private static partial Regex RemoteHeadRegex();

    [GeneratedRegex(@"(?m)^[0-9a-f]{40,64}\s+HEAD\r?$")]
    private static partial Regex RemoteHeadCommitRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex GitBranchRegex();

    [GeneratedRegex(@"(?m)^[ \t]*Gemfile\.lock[ \t]*\r?\n?")]
    private static partial Regex GemfileLockIgnoreRegex();
}
