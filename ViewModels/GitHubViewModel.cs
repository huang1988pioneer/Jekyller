using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class GitHubViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan DeploymentCheckInterval = TimeSpan.FromMinutes(5);
    private readonly IGitHubService _github;
    private readonly IJekyllService _jekyll;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private readonly DeploymentMonitorService _deploymentMonitor;
    private readonly SemaphoreSlim _deploymentCheckGate = new(1, 1);
    private CancellationTokenSource? _deploymentMonitorCts;
    private DeploymentVersionState? _lastDeploymentState;
    private string? _lastExpectedDeploymentId;

    [ObservableProperty]
    public partial string GitStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GhStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RemoteSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PagesSummary { get; set; } = "尚未查詢";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPagesUrl))]
    public partial string PagesUrl { get; set; } = string.Empty;

    public bool HasPagesUrl => !string.IsNullOrWhiteSpace(PagesUrl);

    [ObservableProperty]
    public partial string RepoName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryTargetSummary { get; set; } = "貼上既有 GitHub repository 網址後，Jekyller 會先顯示目標與 Pages 網址。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectNow))]
    public partial bool CanConnectRepository { get; set; }

    [ObservableProperty]
    public partial bool SyncRecommendedSiteUrls { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPrivate { get; set; }

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "Update site via Jekyller";

    [ObservableProperty]
    public partial string DeploymentStatus { get; set; } = "尚未查詢";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeploymentMonitorTitle { get; set; } = "等待第一次部署";

    [ObservableProperty]
    public partial string DeploymentMonitorSummary { get; set; } = "推送網站後，Jekyller 會辨識線上網站是否已更新。";

    [ObservableProperty]
    public partial string DeploymentMonitorSchedule { get; set; } = "每 5 分鐘自動檢查";

    [ObservableProperty]
    public partial bool IsCheckingDeployment { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectNow))]
    public partial bool IsBusy { get; set; }

    public bool CanConnectNow => CanConnectRepository && !IsBusy;

    public GitHubViewModel(
        IGitHubService github,
        IJekyllService jekyll,
        IProjectContext project,
        IDialogService dialogs,
        DeploymentMonitorService deploymentMonitor)
    {
        _github = github;
        _jekyll = jekyll;
        _project = project;
        _dialogs = dialogs;
        _deploymentMonitor = deploymentMonitor;
        _project.ProjectChanged += async (_, _) =>
        {
            _lastDeploymentState = null;
            _lastExpectedDeploymentId = null;
            await RefreshAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        };
        EnsureDeploymentMonitorStarted();
        _ = RefreshAsync();
    }

    partial void OnRepositoryUrlChanged(string value)
    {
        var target = GitHubService.ParseRepositoryTarget(value);
        CanConnectRepository = target.IsValid;
        if (!target.IsValid)
        {
            RepositoryTargetSummary = string.IsNullOrWhiteSpace(value)
                ? "貼上既有 GitHub repository 網址後，Jekyller 會先顯示目標與 Pages 網址。"
                : target.ErrorMessage;
            return;
        }

        RepoName = target.Repository!;
        RepositoryTargetSummary =
            $"Repository：{target.Owner}/{target.Repository}\n" +
            $"網站類型：{(target.IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站")}\n" +
            $"建議 Pages 網址：{target.PagesUrl}\n" +
            $"_config.yml：url={target.JekyllUrl}  baseurl={(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var gitOk = await _github.IsGitAvailableAsync().ConfigureAwait(true);
            var ghOk = await _github.IsGhAvailableAsync().ConfigureAwait(true);
            GitStatus = gitOk ? "Git：已安裝" : "Git：未找到（請安裝 Git for Windows）";
            GhStatus = ghOk ? "GitHub CLI (gh)：已安裝" : "GitHub CLI：未找到（請安裝 gh）";

            if (!_project.HasProject)
            {
                RemoteSummary = "尚未開啟專案";
                RepoName = string.Empty;
                PagesUrl = string.Empty;
                PagesSummary = "尚未查詢";
                DeploymentStatus = "尚未查詢";
                DeploymentMonitorTitle = "尚未選擇網站";
                DeploymentMonitorSummary = "請先在「環境建立」開啟或建立 Jekyll 網站。";
                DeploymentMonitorSchedule = "選擇網站後開始每 5 分鐘檢查";
                return;
            }

            var site = _project.ProjectPath!;
            if (string.IsNullOrWhiteSpace(RepoName))
                RepoName = Path.GetFileName(site.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var info = await _github.GetInfoAsync(site).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(RepositoryUrl) && !string.IsNullOrWhiteSpace(info.RemoteUrl))
                RepositoryUrl = info.RemoteUrl;
            RemoteSummary =
                $"使用者：{info.GhUser ?? "（未登入）"}\n" +
                $"驗證：{(info.GhAuthenticated ? "已登入" : "未登入")}\n" +
                $"分支：{info.Branch ?? "—"}\n" +
                $"Remote：{info.RemoteUrl ?? "（無 origin）"}\n" +
                $"Repo：{(info.Owner is null ? "—" : $"{info.Owner}/{info.Repo}")}";

            await RefreshPagesStatusAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectExistingRepositoryAsync()
    {
        if (!EnsureProject()) return;
        var target = GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!target.IsValid)
        {
            StatusMessage = target.ErrorMessage;
            return;
        }

        IsBusy = true;
        try
        {
            var site = _project.ProjectPath!;
            StatusMessage = "正在確認 GitHub repository 推送權限…";
            var access = await _github.CheckPushAccessAsync(target).ConfigureAwait(true);
            AppendLog(access.Message);
            if (!access.HasAccess)
            {
                StatusMessage = access.Message;
                return;
            }

            if (SyncRecommendedSiteUrls && !string.IsNullOrWhiteSpace(target.JekyllUrl))
            {
                await _github.UpdateSiteUrlsAsync(site, target).ConfigureAwait(true);
                AppendLog($"已將 _config.yml 的 url 設為 {target.JekyllUrl}，baseurl 設為 {(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}");
            }

            StatusMessage = "正在以 production 設定驗證 Jekyll 網站…";
            AppendLog("bundle exec jekyll build（JEKYLL_ENV=production）…");
            var progress = new Progress<string>(AppendLog);
            var build = await _jekyll.BuildAsync(site, progress, production: true).ConfigureAwait(true);
            AppendLog(build.CombinedOutput);
            if (!build.Success)
            {
                StatusMessage = "建置失敗；尚未連結或推送 repository。";
                return;
            }

            var result = await _github.ConnectExistingRepositoryAndPushAsync(
                site,
                target,
                string.IsNullOrWhiteSpace(CommitMessage) ? "Publish site via Jekyller" : CommitMessage.Trim(),
                new Progress<string>(message =>
                {
                    AppendLog(message);
                    StatusMessage = message;
                })).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Success
                ? PushCompletedStatusMessage(result)
                : "連結或部署失敗；請查看操作日誌";
            if (!result.Success) return;

            await RefreshAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        try
        {
            AppendLog("啟動 gh auth login…");
            StatusMessage = "請在瀏覽器完成 GitHub 登入";
            var result = await _github.OpenGhAuthLoginAsync().ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAndDeployAsync()
    {
        if (!EnsureProject()) return;
        if (string.IsNullOrWhiteSpace(RepoName))
        {
            StatusMessage = "請輸入 repository 名稱";
            await _dialogs.ShowMessageAsync("提示", "請輸入 repository 名稱。").ConfigureAwait(true);
            return;
        }

        var existingTarget = GitHubService.ParseRepositoryTarget(RepoName);
        if (existingTarget.IsValid)
        {
            RepositoryUrl = RepoName.Trim();
            AppendLog($"偵測到既有 repository 網址，改用安全連結流程：{existingTarget.Owner}/{existingTarget.Repository}");
            await ConnectExistingRepositoryAsync().ConfigureAwait(true);
            return;
        }

        if (RepoName.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            || RepoName.Contains('/')
            || RepoName.Contains('\\'))
        {
            StatusMessage = $"Repository 網址格式無效：{existingTarget.ErrorMessage}";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var site = _project.ProjectPath!;
            var info = await _github.GetInfoAsync(site).ConfigureAwait(true);
            if (SyncRecommendedSiteUrls)
            {
                var target = GetSiteUrlTarget(info, RepoName.Trim());
                if (target is not null)
                {
                    await _github.UpdateSiteUrlsAsync(site, target).ConfigureAwait(true);
                    AppendLog($"已將 _config.yml 的 url 設為 {target.JekyllUrl}，baseurl 設為 {(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}");
                }
            }

            StatusMessage = "正在以 production 設定驗證 Jekyll 網站…";
            AppendLog("bundle exec jekyll build（JEKYLL_ENV=production）…");
            var build = await _jekyll.BuildAsync(site, new Progress<string>(AppendLog), production: true)
                .ConfigureAwait(true);
            AppendLog(build.CombinedOutput);
            if (!build.Success)
            {
                StatusMessage = "建置失敗；尚未建立或推送 repository。";
                return;
            }

            var result = await _github.CreateRepoAndPushAsync(
                site,
                RepoName.Trim(),
                IsPrivate,
                new Progress<string>(m =>
                {
                    AppendLog(m);
                    StatusMessage = m;
                })).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Success
                ? PushCompletedStatusMessage(result)
                : "部署過程有錯誤，請查看日誌";
            if (!result.Success) return;

            await RefreshAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        if (!EnsureProject()) return;

        var site = _project.ProjectPath!;
        var info = await _github.GetInfoAsync(site).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var candidate = !string.IsNullOrWhiteSpace(RepositoryUrl) ? RepositoryUrl : RepoName;
            var target = GitHubService.ParseRepositoryTarget(candidate);
            if (target.IsValid)
            {
                RepositoryUrl = candidate;
                AppendLog($"尚未設定 origin；改用安全連結流程：{target.Owner}/{target.Repository}");
                await ConnectExistingRepositoryAsync().ConfigureAwait(true);
                return;
            }

            StatusMessage = "尚未連結 GitHub repository。請先在上方貼上完整 Repository URL，再按「連結、推送並啟用 Pages」。";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            if (SyncRecommendedSiteUrls)
            {
                var target = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
                if (target.IsValid)
                {
                    await _github.UpdateSiteUrlsAsync(site, target).ConfigureAwait(true);
                    AppendLog($"已依 origin 同步 _config.yml：url={target.JekyllUrl}，baseurl={(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}");
                }
            }

            StatusMessage = "正在以 production 設定建置 Jekyll 網站…";
            AppendLog("bundle exec jekyll build（JEKYLL_ENV=production）…");
            var build = await _jekyll.BuildAsync(site, new Progress<string>(AppendLog), production: true)
                .ConfigureAwait(true);
            AppendLog(build.CombinedOutput);
            if (!build.Success)
            {
                StatusMessage = "建置失敗；已停止提交與推送。請依日誌修正網站內容。";
                return;
            }

            var result = await _github.PushAsync(
                site,
                CommitMessage,
                new Progress<string>(m =>
                {
                    AppendLog(m);
                    StatusMessage = m;
            })).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Success ? "推送完成；正在等待 Actions 部署確認" : "推送失敗";
            if (!result.Success) return;

            await RefreshPagesStatusAsync(updateStatusMessage: false).ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (!EnsureProject()) return;
        IsBusy = true;
        try
        {
            AppendLog("從 origin 拉取最新變更…");
            var result = await _github.PullAsync(_project.ProjectPath!, new Progress<string>(AppendLog))
                .ConfigureAwait(true);
            AppendLog(result.Success ? "Pull 完成。" : "Pull 失敗：\n" + result.CombinedOutput);
            StatusMessage = result.Success ? "Pull 完成" : "Pull 失敗";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EnablePagesAsync()
    {
        if (!EnsureProject()) return;
        IsBusy = true;
        try
        {
            var workflowMessage = await _github.EnsureGitHubActionsWorkflowAsync(_project.ProjectPath!)
                .ConfigureAwait(true);
            AppendLog(workflowMessage);
            var result = await _github.EnablePagesFromActionsAsync(_project.ProjectPath!).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Success ? "已請求啟用 GitHub Pages" : "啟用失敗";
            if (!result.Success) return;

            await RefreshPagesStatusAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshPagesStatusAsync(bool updateStatusMessage = true)
    {
        if (!_project.HasProject) return;

        var status = await _github.GetPagesStatusAsync(_project.ProjectPath!).ConfigureAwait(true);
        PagesUrl = status.HtmlUrl;
        PagesSummary =
            $"啟用：{(status.IsEnabled ? "是" : "否")}\n" +
            $"狀態：{(string.IsNullOrWhiteSpace(status.Status) ? "—" : status.Status)}\n" +
            $"建置類型：{(string.IsNullOrWhiteSpace(status.BuildType) ? "—" : status.BuildType)}\n" +
            $"來源分支：{(string.IsNullOrWhiteSpace(status.SourceBranch) ? "—" : status.SourceBranch)}\n" +
            $"網址：{(string.IsNullOrWhiteSpace(status.HtmlUrl) ? "—" : status.HtmlUrl)}\n" +
            $"CNAME：{(string.IsNullOrWhiteSpace(status.Cname) ? "—" : status.Cname)}\n" +
            $"{status.Message}";
        if (updateStatusMessage)
            StatusMessage = status.Message;
        DeploymentStatus = await _github.GetLatestDeploymentAsync(_project.ProjectPath!).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task CheckDeploymentNowAsync() =>
        CheckDeploymentVersionAsync(manual: true, CancellationToken.None);

    [RelayCommand]
    private void OpenPagesUrl()
    {
        if (string.IsNullOrWhiteSpace(PagesUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PagesUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddWorkflowOnlyAsync()
    {
        if (!EnsureProject()) return;
        var message = await _github.EnsureGitHubActionsWorkflowAsync(_project.ProjectPath!).ConfigureAwait(true);
        StatusMessage = message;
        AppendLog(message);
    }

    public void Dispose()
    {
        _deploymentMonitorCts?.Cancel();
        _deploymentMonitorCts?.Dispose();
        _deploymentMonitorCts = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureDeploymentMonitorStarted()
    {
        if (_deploymentMonitorCts is not null) return;
        _deploymentMonitorCts = new CancellationTokenSource();
        _ = MonitorDeploymentLoopAsync(_deploymentMonitorCts.Token);
    }

    private async Task MonitorDeploymentLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckDeploymentVersionAsync(manual: false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(DeploymentCheckInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
    }

    private async Task CheckDeploymentVersionAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!await _deploymentCheckGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        IsCheckingDeployment = true;
        DeploymentMonitorTitle = "正在檢查線上版本…";
        DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 正在連線";
        try
        {
            if (!_project.HasProject)
            {
                DeploymentMonitorTitle = "尚未選擇網站";
                DeploymentMonitorSummary = "請先在「環境建立」開啟或建立 Jekyll 網站。";
                DeploymentMonitorSchedule = "選擇網站後開始每 5 分鐘檢查";
                return;
            }

            var site = _project.ProjectPath!;
            if (string.IsNullOrWhiteSpace(PagesUrl))
            {
                var pages = await _github.GetPagesStatusAsync(site, cancellationToken).ConfigureAwait(false);
                PagesUrl = pages.HtmlUrl;
            }

            var result = await _deploymentMonitor.CheckAsync(site, PagesUrl, cancellationToken).ConfigureAwait(false);
            var stateChanged = result.State != _lastDeploymentState
                               || !string.Equals(result.ExpectedDeploymentId, _lastExpectedDeploymentId,
                                   StringComparison.Ordinal);

            DeploymentMonitorTitle = result.State switch
            {
                DeploymentVersionState.Latest => "線上網站已是最新版本",
                DeploymentVersionState.Previous => "線上網站仍是上一版本",
                DeploymentVersionState.Unavailable => "暫時無法檢查",
                _ => "等待下一次部署"
            };
            DeploymentMonitorSummary = result.Message;
            DeploymentMonitorSchedule =
                $"每 5 分鐘自動檢查 · 上次：{result.CheckedAt.LocalDateTime:yyyy/MM/dd HH:mm:ss}";

            if (manual || stateChanged)
                AppendLog($"線上版本監控：{result.Message}");

            if (result.State == DeploymentVersionState.Latest && stateChanged)
                StatusMessage = "網站已更新：線上內容是最新版本。";
            else if (result.State == DeploymentVersionState.Previous && stateChanged)
                StatusMessage = "線上網站仍是上一版本，將在 5 分鐘後再次檢查。";
            else if (manual)
                StatusMessage = result.Message;

            _lastDeploymentState = result.State;
            _lastExpectedDeploymentId = result.ExpectedDeploymentId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch (Exception ex)
        {
            DeploymentMonitorTitle = "暫時無法檢查";
            DeploymentMonitorSummary = $"檢查線上版本時發生錯誤：{ex.Message}";
            DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 稍後重試";
            if (manual) AppendLog(DeploymentMonitorSummary);
        }
        finally
        {
            IsCheckingDeployment = false;
            _deploymentCheckGate.Release();
        }
    }

    private static GitHubRepositoryTarget? GetSiteUrlTarget(GitRemoteInfo info, string repoName)
    {
        if (!string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var remoteTarget = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
            if (remoteTarget.IsValid)
                return remoteTarget;
        }

        if (!string.IsNullOrWhiteSpace(info.GhUser) && !string.IsNullOrWhiteSpace(repoName))
        {
            var guessed = GitHubService.ParseRepositoryTarget($"https://github.com/{info.GhUser}/{repoName}");
            if (guessed.IsValid)
                return guessed;
        }

        return null;
    }

    private bool EnsureProject()
    {
        if (_project.HasProject) return true;
        _ = _dialogs.ShowMessageAsync("提示", "請先開啟或建立專案。");
        return false;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {line.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? formatted : Log + Environment.NewLine + formatted;
    }

    private static string PushCompletedStatusMessage(ProcessResult result)
    {
        return result.CombinedOutput.Contains("沒有管理 GitHub Pages 設定所需的 admin 權限", StringComparison.OrdinalIgnoreCase)
            ? "網站檔案已推送；目前帳號無法自動啟用 Pages，請由 Repository 擁有者在 Settings > Pages 將 Source 設為 GitHub Actions。"
            : "已推送並已請求啟用 GitHub Pages；正在等待 Actions 部署確認";
    }
}
