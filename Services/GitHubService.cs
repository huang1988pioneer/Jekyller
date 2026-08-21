using System.Text.Json;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IGitHubService
{
    Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> IsGhAvailableAsync(CancellationToken cancellationToken = default);
    Task<GitRemoteInfo> GetInfoAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<(bool HasAccess, string Message)> CheckPushAccessAsync(
        GitHubRepositoryTarget target,
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
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> PullAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string> GetLatestDeploymentAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<GitHubPagesStatus> GetPagesStatusAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ProcessResult> EnablePagesFromActionsAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string?> DetectRemoteAsync(string projectPath, CancellationToken cancellationToken = default);
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
            return InvalidTarget("請貼上 GitHub repository 網址。");

        var ssh = GitHubSshRemoteRegex().Match(value);
        if (ssh.Success)
        {
            value = $"https://github.com/{ssh.Groups["owner"].Value}/{ssh.Groups["repo"].Value}";
        }
        else if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
                ? $"https://{value}"
                : $"https://github.com/{value.TrimStart('/')}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidTarget("僅支援 https://github.com/owner/repository 網址。");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
            return InvalidTarget("網址必須指向 repository 首頁，不可包含 issues、settings 等子路徑。");

        var owner = Uri.UnescapeDataString(segments[0]);
        var repository = Uri.UnescapeDataString(segments[1]);
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];

        if (!GitHubOwnerRegex().IsMatch(owner) || !GitHubRepositoryRegex().IsMatch(repository))
            return InvalidTarget("GitHub owner 或 repository 名稱格式無效。");

        var userSite = repository.Equals($"{owner}.github.io", StringComparison.OrdinalIgnoreCase);
        var host = $"https://{owner.ToLowerInvariant()}.github.io";
        return new GitHubRepositoryTarget
        {
            IsValid = true,
            Owner = owner,
            Repository = repository,
            CanonicalUrl = $"https://github.com/{owner}/{repository}.git",
            PagesUrl = userSite ? $"{host}/" : $"{host}/{repository}/",
            JekyllUrl = host,
            JekyllBaseUrl = userSite ? string.Empty : $"/{repository}",
            IsUserOrOrganizationSite = userSite
        };
    }

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

    public async Task<GitRemoteInfo> GetInfoAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var data = new GitRemoteInfo();

        var branch = await _processRunner.RunAsync(
            "git", "branch --show-current", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (branch.Success)
            data.Branch = branch.StdOut.Trim();

        var remote = await _processRunner.RunAsync(
            "git", "remote get-url origin", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remote.Success)
        {
            data.RemoteUrl = remote.StdOut.Trim();
            var (owner, repo) = ParseGitHubRemote(data.RemoteUrl);
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

        var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;

        progress?.Report("提交檔案…");
        await CommitAllAsync(projectPath, "Initial commit via Jekyller", progress, cancellationToken).ConfigureAwait(false);

        var visibility = isPrivate ? "--private" : "--public";
        progress?.Report($"建立 GitHub repository：{repoName}…");

        var remote = await _processRunner.RunAsync(
            "git", "remote get-url origin", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!remote.Success)
        {
            var create = await _processRunner.RunAsync(
                "gh",
                $"repo create \"{repoName}\" {visibility} --source=. --remote=origin --push",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 180_000).ConfigureAwait(false);

            if (!create.Success)
                return create;
        }
        else
        {
            progress?.Report("推送到 origin…");
            var push = await _processRunner.RunAsync(
                "git", "push -u origin HEAD", projectPath, progress, cancellationToken, timeoutMs: 180_000)
                .ConfigureAwait(false);
            if (!push.Success)
                return push;
        }

        progress?.Report("啟用 GitHub Pages（GitHub Actions）…");
        var pages = await EnablePagesFromActionsAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        return new ProcessResult
        {
            ExitCode = pages.Success ? 0 : pages.ExitCode,
            StdOut = $"Repo ready.\n{pages.CombinedOutput}",
            StdErr = pages.Success ? string.Empty : pages.StdErr
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

        var remote = await _processRunner.RunAsync(
            "git", "remote get-url origin", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remote.Success)
        {
            var (existingOwner, existingRepository) = ParseGitHubRemote(remote.StdOut.Trim());
            if (string.IsNullOrWhiteSpace(existingOwner)
                || string.IsNullOrWhiteSpace(existingRepository)
                || !existingOwner.Equals(target.Owner, StringComparison.OrdinalIgnoreCase)
                || !existingRepository.Equals(target.Repository, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 origin 已指向其他 repository：{remote.StdOut.Trim()}。為避免推錯位置，Jekyller 未修改 origin。"
                };
            }
        }
        else
        {
            progress?.Report($"連結 origin：{target.Owner}/{target.Repository}…");
            var addRemote = await _processRunner.RunAsync(
                "git",
                $"remote add origin \"{target.CanonicalUrl}\"",
                projectPath,
                progress,
                cancellationToken,
                timeoutMs: 15_000).ConfigureAwait(false);
            if (!addRemote.Success) return addRemote;
        }

        progress?.Report("抓取遠端預設分支…");
        var fetch = await _processRunner.RunAsync(
            "git", "fetch origin --prune", projectPath, progress, cancellationToken, timeoutMs: 120_000)
            .ConfigureAwait(false);
        if (!fetch.Success) return fetch;

        var remoteHead = await _processRunner.RunAsync(
            "git", "ls-remote --symref origin HEAD", projectPath, timeoutMs: 30_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var branchMatch = RemoteHeadRegex().Match(remoteHead.StdOut);
        var remoteBranch = branchMatch.Success ? branchMatch.Groups["branch"].Value : "main";
        if (!GitBranchRegex().IsMatch(remoteBranch))
            return new ProcessResult { ExitCode = -1, StdErr = "遠端預設分支名稱格式不安全，已停止操作。" };
        var remoteHasCommit = RemoteHeadCommitRegex().IsMatch(remoteHead.StdOut);

        var localHead = await _processRunner.RunAsync(
            "git", "rev-parse --verify HEAD", projectPath, timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (remoteHasCommit && !localHead.Success)
        {
            progress?.Report($"以遠端 {remoteBranch} 為基準，保留本機未追蹤網站檔案…");
            var checkout = await _processRunner.RunAsync(
                "git",
                $"checkout -B \"{remoteBranch}\" --track \"origin/{remoteBranch}\"",
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
            progress?.Report($"合併遠端 {remoteBranch}（允許初始 README 歷史）…");
            var merge = await _processRunner.RunAsync(
                "git",
                $"merge \"origin/{remoteBranch}\" --allow-unrelated-histories --no-edit",
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

        progress?.Report("加入 GitHub Actions workflow 並提交網站…");
        var workflowMessage = await EnsureGitHubActionsWorkflowAsync(projectPath, cancellationToken).ConfigureAwait(false);
        progress?.Report(workflowMessage);
        var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(projectPath, commitMessage, progress, cancellationToken).ConfigureAwait(false);
        if (!commit.Success) return commit;

        progress?.Report($"推送到 {target.Owner}/{target.Repository}…");
        var push = await _processRunner.RunAsync(
            "git",
            $"push -u origin HEAD:\"{remoteBranch}\"",
            projectPath,
            progress,
            cancellationToken,
            timeoutMs: 180_000).ConfigureAwait(false);
        if (!push.Success) return push;

        progress?.Report("啟用 GitHub Pages（Actions）…");
        return await EnablePagesFromActionsAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> PushAsync(
        string projectPath,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("提交變更…");
        var workflowMessage = await EnsureGitHubActionsWorkflowAsync(projectPath, cancellationToken).ConfigureAwait(false);
        progress?.Report(workflowMessage);
        var markerError = await PrepareDeploymentMarkerAsync(projectPath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(projectPath, commitMessage, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(commit.CombinedOutput);

        progress?.Report("git push…");
        return await _processRunner.RunAsync(
            "git", "push", projectPath, progress, cancellationToken, timeoutMs: 180_000).ConfigureAwait(false);
    }

    public Task<ProcessResult> PullAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
        => _processRunner.RunAsync(
            "git", "pull --ff-only", projectPath, output, cancellationToken, timeoutMs: 120_000);

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
                    "built" => "網站已成功建置並上線。",
                    "building" => "正在建置中…",
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
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "找不到 GitHub remote（origin）。請先建立或連結 repository。"
            };
        }

        var permission = await _processRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo} --jq .permissions.admin",
            projectPath,
            output,
            cancellationToken,
            timeoutMs: 30_000).ConfigureAwait(false);

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
                ExitCode = -1,
                StdOut = "網站檔案與 GitHub Actions workflow 已成功推送。",
                StdErr = $"目前登入帳號具有 {info.Owner}/{info.Repo} 的推送權限，但沒有管理 GitHub Pages 設定所需的 admin 權限。\n" +
                         "請 Repository 擁有者開啟 Settings > Pages，在 Build and deployment 的 Source 選擇 GitHub Actions；完成後回到 Jekyller 按「查詢 Pages 狀態」。"
            };
        }

        var current = await _processRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo}/pages",
            projectPath,
            output,
            cancellationToken,
            timeoutMs: 30_000).ConfigureAwait(false);

        var pagesExist = current.Success;
        if (!pagesExist
            && !current.CombinedOutput.Contains("404", StringComparison.OrdinalIgnoreCase)
            && !current.CombinedOutput.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return current;
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

    public async Task<string?> DetectRemoteAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "git",
            "remote get-url origin",
            projectPath,
            timeoutMs: 10_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<(string Owner, string Repo)?> ParseOwnerRepoAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var remote = await DetectRemoteAsync(projectPath, cancellationToken).ConfigureAwait(false);
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

    private static (string? Owner, string? Repo) ParseGitHubRemote(string url)
    {
        var m = GitHubRemoteRegex().Match(url);
        return m.Success ? (m.Groups["owner"].Value, m.Groups["repo"].Value) : (null, null);
    }

    private static GitHubRepositoryTarget InvalidTarget(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };

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

    [GeneratedRegex(@"^git@github\.com:(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubSshRemoteRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex GitHubOwnerRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepositoryRegex();

    [GeneratedRegex(@"ref:\s+refs/heads/(?<branch>[^\s]+)\s+HEAD")]
    private static partial Regex RemoteHeadRegex();

    [GeneratedRegex(@"(?m)^[0-9a-f]{40,64}\s+HEAD$")]
    private static partial Regex RemoteHeadCommitRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex GitBranchRegex();

    [GeneratedRegex(@"(?m)^[ \t]*Gemfile\.lock[ \t]*\r?\n?")]
    private static partial Regex GemfileLockIgnoreRegex();
}
