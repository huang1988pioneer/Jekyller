using System.Text.Json;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Services;

public interface IGitHubService
{
    Task<ProcessResult> InitRepositoryAsync(string projectPath, IProgress<string>? output = null, CancellationToken cancellationToken = default);
    Task<ProcessResult> CreateAndPushAsync(
        string projectPath,
        string repoName,
        bool isPrivate,
        string commitMessage,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> CommitAndPushAsync(
        string projectPath,
        string commitMessage,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> PullAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<ProcessResult> ConfigureActionsPagesAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string> GetLatestDeploymentAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<GitHubPagesStatus> GetPagesStatusAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ProcessResult> EnablePagesAsync(
        string projectPath,
        string branch = "main",
        string path = "/",
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string?> DetectRemoteAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<(string Owner, string Repo)?> ParseOwnerRepoAsync(string projectPath, CancellationToken cancellationToken = default);
}

public sealed class GitHubService(IProcessRunner processRunner) : IGitHubService
{
    public async Task<ProcessResult> InitRepositoryAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(projectPath, ".git")))
        {
            var init = await processRunner.RunAsync("git", "init -b main", projectPath, output, cancellationToken)
                .ConfigureAwait(false);
            if (!init.Success)
                return init;
        }

        await EnsureGitignoreAsync(projectPath, cancellationToken).ConfigureAwait(false);
        return new ProcessResult { ExitCode = 0, StdOut = "Git repository ready." };
    }

    public async Task<ProcessResult> CreateAndPushAsync(
        string projectPath,
        string repoName,
        bool isPrivate,
        string commitMessage,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var init = await InitRepositoryAsync(projectPath, output, cancellationToken).ConfigureAwait(false);
        if (!init.Success)
            return init;

        var add = await processRunner.RunAsync("git", "add -A", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
        if (!add.Success)
            return add;

        // Commit may fail if nothing to commit; continue.
        await processRunner.RunAsync(
            "git",
            $"commit -m \"{Escape(commitMessage)}\"",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);

        var visibility = isPrivate ? "--private" : "--public";
        var create = await processRunner.RunAsync(
            "gh",
            $"repo create \"{repoName}\" {visibility} --source=. --remote=origin --push",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);

        return create;
    }

    public async Task<ProcessResult> CommitAndPushAsync(
        string projectPath,
        string commitMessage,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var add = await processRunner.RunAsync("git", "add -A", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
        if (!add.Success)
            return add;

        await processRunner.RunAsync(
            "git",
            $"commit -m \"{Escape(commitMessage)}\"",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);

        // Try main then master
        var push = await processRunner.RunAsync("git", "push -u origin HEAD", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
        return push;
    }

    public Task<ProcessResult> PullAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
        => processRunner.RunAsync("git", "pull --ff-only", projectPath, output, cancellationToken);

    public async Task<ProcessResult> ConfigureActionsPagesAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
            return new ProcessResult { ExitCode = 1, StdErr = "找不到 GitHub remote（origin）。請先建立並推送 repository。" };

        var workflowPath = Path.Combine(projectPath, ".github", "workflows", "jekyll.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, GitHubPagesWorkflow, cancellationToken).ConfigureAwait(false);
        output?.Report("已建立 .github/workflows/jekyll.yml。");

        var (owner, repo) = ownerRepo.Value;
        var enable = await processRunner.RunAsync(
            "gh",
            $"api -X POST \"repos/{owner}/{repo}/pages\" -f build_type=workflow",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);

        // GitHub returns 409 when Pages is already enabled. Updating the build type is then enough.
        if (!enable.Success && !enable.CombinedOutput.Contains("409", StringComparison.OrdinalIgnoreCase))
            return enable;

        var update = await processRunner.RunAsync(
            "gh",
            $"api -X PUT \"repos/{owner}/{repo}/pages\" -f build_type=workflow",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);

        if (update.Success)
            output?.Report("GitHub Pages 已設定為 GitHub Actions 部署。請提交並推送工作流程。");

        return update;
    }

    public async Task<string> GetLatestDeploymentAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
            return "尚未設定 GitHub remote。";

        var (owner, repo) = ownerRepo.Value;
        var result = await processRunner.RunAsync(
            "gh",
            $"run list --repo \"{owner}/{repo}\" --workflow jekyll.yml --limit 1 --json status,conclusion,displayTitle,url,createdAt",
            projectPath,
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
            var title = run.GetProperty("displayTitle").GetString() ?? "Deploy Jekyll to GitHub Pages";
            var url = run.GetProperty("url").GetString() ?? string.Empty;
            return $"{title}\n狀態：{status}{(string.IsNullOrWhiteSpace(conclusion) ? "" : $" ({conclusion})")}\n{url}";
        }
        catch (JsonException)
        {
            return "無法解析 GitHub Actions 部署狀態。";
        }
    }

    public async Task<GitHubPagesStatus> GetPagesStatusAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
        {
            return new GitHubPagesStatus
            {
                Success = false,
                Message = "找不到 GitHub remote（origin）。請先建立並推送 repository。"
            };
        }

        var (owner, repo) = ownerRepo.Value;
        var result = await processRunner.RunAsync(
            "gh",
            $"api \"repos/{owner}/{repo}/pages\"",
            projectPath,
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

            var branch = string.Empty;
            var path = string.Empty;
            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                branch = source.TryGetProperty("branch", out var b) ? b.GetString() ?? string.Empty : string.Empty;
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
                Cname = cname,
                Message = $"GitHub Pages 狀態：{status}"
            };
        }
        catch (Exception ex)
        {
            return new GitHubPagesStatus
            {
                Success = false,
                Message = "解析 Pages API 回應失敗：" + ex.Message
            };
        }
    }

    public async Task<ProcessResult> EnablePagesAsync(
        string projectPath,
        string branch = "main",
        string path = "/",
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var ownerRepo = await ParseOwnerRepoAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (ownerRepo is null)
            return new ProcessResult { ExitCode = 1, StdErr = "找不到 origin remote" };

        var (owner, repo) = ownerRepo.Value;
        var body = $"{{\"build_type\":\"legacy\",\"source\":{{\"branch\":\"{branch}\",\"path\":\"{path}\"}}}}";
        // Use --input - via shell is hard; pass raw fields with gh api -f
        return await processRunner.RunAsync(
            "gh",
            $"api -X POST \"repos/{owner}/{repo}/pages\" -f build_type=legacy -f source[branch]={branch} -f source[path]={path}",
            projectPath,
            output,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> DetectRemoteAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "git",
            "remote get-url origin",
            projectPath,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<(string Owner, string Repo)?> ParseOwnerRepoAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var remote = await DetectRemoteAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(remote))
            return null;

        // nina.v@example.com:owner/repo.git or https://github.com/owner/repo.git
        var m = Regex.Match(remote, @"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        return (m.Groups["owner"].Value, m.Groups["repo"].Value);
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
                                Gemfile.lock
                                """;

        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, defaults, cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var lines = defaults.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var missing = lines.Where(l => !existing.Contains(l, StringComparison.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0)
            return;

        var append = (existing.EndsWith('\n') ? string.Empty : Environment.NewLine)
                     + string.Join(Environment.NewLine, missing)
                     + Environment.NewLine;
        await File.AppendAllTextAsync(path, append, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value)
        => value.Replace("\"", "\\\"");

    private const string GitHubPagesWorkflow = """
        name: Deploy Jekyll to GitHub Pages

        on:
          push:
            branches: ["main"]
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
                uses: actions/checkout@v6
              - name: Setup Pages
                uses: actions/configure-pages@v5
              - name: Build with Jekyll
                uses: actions/jekyll-build-pages@v1
                with:
                  source: ./
                  destination: ./_site
              - name: Upload artifact
                uses: actions/upload-pages-artifact@v4

          deploy:
            environment:
              name: github-pages
              url: ${{ steps.deployment.outputs.page_url }}
            runs-on: ubuntu-latest
            needs: build
            steps:
              - name: Deploy
                id: deployment
                uses: actions/deploy-pages@v4
        """;
}
