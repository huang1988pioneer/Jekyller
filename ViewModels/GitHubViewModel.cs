using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Helpers;
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
    private readonly ISettingsService _settings;
    private bool _changingPlatform;
    private bool _initialized;
    private readonly SemaphoreSlim _deploymentCheckGate = new(1, 1);
    private CancellationTokenSource? _deploymentMonitorCts;
    private DeploymentVersionState? _lastDeploymentState;
    private string? _lastExpectedDeploymentId;
    private string? _lastAutoCloneSiteName;
    private GitRemoteInfo? _lastRemoteInfo;

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
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string RepositoryUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryTargetSummary { get; set; } = "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository 網址。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string CloneParentDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string CloneSiteName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloneTargetSummary { get; set; } = "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址，或從下方清單選擇已上線的 GitHub 網站。";

    [ObservableProperty]
    public partial string PagesSitesMessage { get; set; } = "尚未開啟本機專案時，會自動列出你 GitHub 上已啟用 Pages 的網站。";

    [ObservableProperty]
    public partial bool HasPagesSites { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowGitHubProjectTools))]
    public partial bool HasLocalProject { get; set; }

    [ObservableProperty]
    public partial bool IsGitHubRemote { get; set; }

    [ObservableProperty]
    public partial string HostingPlatformLabel { get; set; } = "Git 平台";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnectNow))]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
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
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial bool IsBusy { get; set; }

    public bool CanConnectNow => CanConnectRepository && !IsBusy;

    public bool CanCloneNow =>
        CanConnectRepository
        && !IsBusy
        && !string.IsNullOrWhiteSpace(CloneParentDirectory)
        && !string.IsNullOrWhiteSpace(CloneSiteName);

    public bool IsGitHubPlatformSelected => SelectedGitPlatform == GitHostingPlatform.GitHub;

    public bool CanShowGitHubProjectTools => HasLocalProject && IsGitHubPlatformSelected;

    public string PushToolsCaption => SelectedGitPlatform switch
    {
        GitHostingPlatform.GitLab =>
            "建置後提交並推送來源；GitLab Pages CI 會用 Ruby / Bundler 建置 Jekyll 並發布 public/。" +
            "若 Settings > Pages 是 Everyone With Access，訪客需登入；要公開請改成 Everyone。GitLab 快取通常不到 1 分鐘。",
        GitHostingPlatform.Codeberg =>
            "建置後把 _site 推到 Codeberg 的 pages 分支。請在 Codeberg 設定 Pages Webhook；來源分支不會覆寫遠端。",
        GitHostingPlatform.Bitbucket =>
            "建置後把 _site 推到 <workspace>.bitbucket.io 的預設分支。Bitbucket Cloud 沒有專案層級 Pages。",
        _ =>
            "建置後提交並推送，由 GitHub Actions 部署 Pages。"
    };

    public ObservableCollection<GitHubPagesSiteItem> PagesSites { get; } = [];
    public IReadOnlyList<GitHostingPlatform> GitPlatforms { get; } = Enum.GetValues<GitHostingPlatform>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGitHubPlatformSelected))]
    [NotifyPropertyChangedFor(nameof(CanShowGitHubProjectTools))]
    [NotifyPropertyChangedFor(nameof(PushToolsCaption))]
    public partial GitHostingPlatform SelectedGitPlatform { get; set; } = GitHostingPlatform.GitHub;

    public GitHubViewModel(
        IGitHubService github,
        IJekyllService jekyll,
        IProjectContext project,
        IDialogService dialogs,
        DeploymentMonitorService deploymentMonitor,
        ISettingsService settings)
    {
        _github = github;
        _jekyll = jekyll;
        _project = project;
        _dialogs = dialogs;
        _deploymentMonitor = deploymentMonitor;
        _settings = settings;
        if (Enum.TryParse<GitHostingPlatform>(_settings.Current.SelectedGitPlatform, true, out var platform))
            SelectedGitPlatform = platform;
        _changingPlatform = true;
        RepositoryUrl = _settings.GetRepositoryUrl(SelectedGitPlatform.ToString());
        _changingPlatform = false;
        HasLocalProject = _project.HasProject;
        if (string.IsNullOrWhiteSpace(CloneParentDirectory))
            CloneParentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _project.ProjectChanged += async (_, _) =>
        {
            _lastDeploymentState = null;
            _lastExpectedDeploymentId = null;
            HasLocalProject = _project.HasProject;
            await RefreshAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        };
        EnsureDeploymentMonitorStarted();
        _initialized = true;
        _ = RefreshAsync();
    }

    partial void OnRepositoryUrlChanged(string value)
    {
        if (!_changingPlatform)
            _settings.SetRepositoryUrl(SelectedGitPlatform.ToString(), value);
        var target = GitHubService.ParseRepositoryTarget(value);
        CanConnectRepository = target.IsValid;
        if (!target.IsValid)
        {
            RepositoryTargetSummary = string.IsNullOrWhiteSpace(value)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository 網址。"
                : target.ErrorMessage;
            UpdateCloneTargetSummary();
            return;
        }

        if (target.Platform != SelectedGitPlatform)
        {
            CanConnectRepository = false;
            RepositoryTargetSummary = $"目前選擇 {SelectedGitPlatform}，但網址屬於 {target.PlatformLabel}。請切換平台或更正網址。";
            UpdateCloneTargetSummary();
            return;
        }

        RepoName = target.Repository!;
        if (string.IsNullOrWhiteSpace(CloneSiteName) || CloneSiteName == _lastAutoCloneSiteName)
            CloneSiteName = target.Repository!;
        _lastAutoCloneSiteName = target.Repository;
        RepositoryTargetSummary =
            $"平台：{target.PlatformLabel}\n" +
            $"Repository：{target.Owner}/{target.Repository}\n" +
            (string.IsNullOrWhiteSpace(target.PagesUrl)
                ? "此平台不提供可自動推定的 Pages 網址；Jekyller 只處理 Git 連結與推送。"
                : $"建議 Pages 網址：{target.PagesUrl}\n_config.yml：url={target.JekyllUrl}  baseurl={(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}");
        UpdateCloneTargetSummary();
        UpdateLocalRepositorySummary(target);
    }

    partial void OnSelectedGitPlatformChanged(GitHostingPlatform value)
    {
        _settings.SetSelectedGitPlatform(value.ToString());
        _changingPlatform = true;
        RepositoryUrl = _settings.GetRepositoryUrl(value.ToString());
        _changingPlatform = false;
        OnRepositoryUrlChanged(RepositoryUrl);
        if (_initialized)
            _ = RefreshAsync();
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

            HasLocalProject = _project.HasProject;
            if (!_project.HasProject)
            {
                IsGitHubRemote = false;
                HostingPlatformLabel = "Git 平台";
                RemoteSummary = "尚未開啟本機專案。若 GitHub Pages 已有網站，可在下方複製到本機。";
                RepoName = string.Empty;
                PagesUrl = string.Empty;
                PagesSummary = "尚未開啟本機專案";
                DeploymentStatus = "尚未查詢";
                DeploymentMonitorTitle = "尚未選擇網站";
                DeploymentMonitorSummary = "請先複製 GitHub Pages 網站，或到「環境建立」開啟／建立 Jekyll 網站。";
                DeploymentMonitorSchedule = "選擇網站後開始每 5 分鐘檢查";
                if (ghOk)
                    await LoadPagesSitesCoreAsync().ConfigureAwait(true);
                return;
            }

            var site = _project.ProjectPath!;
            if (string.IsNullOrWhiteSpace(RepoName))
                RepoName = Path.GetFileName(site.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var info = await _github.GetInfoAsync(site, SelectedGitPlatform).ConfigureAwait(true);
            _lastRemoteInfo = info;
            var remoteTarget = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
            var configuredTarget = GitHubService.ParseRepositoryTarget(RepositoryUrl);
            var selectedTarget = GitPlatformSelection.GetSelectedPlatformTarget(
                SelectedGitPlatform,
                configuredTarget,
                remoteTarget);
            IsGitHubRemote = GitPlatformSelection.ShouldUseGitHubPagesApi(
                SelectedGitPlatform,
                selectedTarget,
                remoteTarget);
            HostingPlatformLabel = selectedTarget.PlatformLabel;
            if (GitPlatformSelection.ShouldAdoptRemote(
                    SelectedGitPlatform,
                    RepositoryUrl,
                    remoteTarget)
                && !string.IsNullOrWhiteSpace(info.RemoteUrl))
                RepositoryUrl = info.RemoteUrl;
            RemoteSummary = BuildRemoteSummary(info, selectedTarget, remoteTarget);

            await RefreshPagesStatusAsync().ConfigureAwait(true);
            await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseCloneParentAsync()
    {
        var folder = await _dialogs.PickFolderAsync("選擇要複製到的本機父資料夾").ConfigureAwait(true);
        if (folder is not null)
            CloneParentDirectory = folder;
    }

    [RelayCommand]
    private async Task ListPagesSitesAsync()
    {
        IsBusy = true;
        try
        {
            await LoadPagesSitesCoreAsync().ConfigureAwait(true);
            StatusMessage = PagesSitesMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CloneListedSiteAsync(GitHubPagesSiteItem? site)
    {
        if (site is null)
            return Task.CompletedTask;

        RepositoryUrl = site.RepositoryUrl;
        CloneSiteName = site.Repository;
        _lastAutoCloneSiteName = site.Repository;
        return CloneFromGitHubAsync();
    }

    [RelayCommand]
    private async Task CloneFromGitHubAsync()
    {
        var target = GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!target.IsValid)
        {
            StatusMessage = target.ErrorMessage;
            return;
        }

        if (target.Platform != SelectedGitPlatform)
        {
            StatusMessage = $"目前選擇 {SelectedGitPlatform}，但網址屬於 {target.PlatformLabel}。請切換平台或更正網址。";
            return;
        }

        if (!GitHubCloneDestination.TryCreatePath(CloneParentDirectory, CloneSiteName, out var dest, out var pathError))
        {
            StatusMessage = pathError;
            await _dialogs.ShowMessageAsync("無法複製", pathError).ConfigureAwait(true);
            return;
        }

        if (!GitHubCloneDestination.IsVacant(dest))
        {
            StatusMessage = $"目標資料夾不是空的：{dest}。請換一個資料夾名稱，以免覆蓋現有檔案。";
            await _dialogs.ShowMessageAsync("無法複製", StatusMessage).ConfigureAwait(true);
            return;
        }

        if (_project.HasProject)
        {
            var ok = await _dialogs.ConfirmAsync(
                "複製後會改開啟新資料夾",
                $"目前已開啟本機網站：\n{_project.ProjectPath}\n\n複製完成後會改開啟：\n{dest}").ConfigureAwait(true);
            if (!ok) return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"正在複製 {target.Owner}/{target.Repository}…";
            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await _github.CloneRepositoryAsync(target, dest, progress).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            if (!result.Success)
            {
                StatusMessage = "複製失敗；請查看操作日誌";
                return;
            }

            await FinishLocalCloneAsync(dest, progress).ConfigureAwait(true);
            StatusMessage = $"已複製到 {dest}，並開啟為目前專案。";
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
        if (target.Platform != SelectedGitPlatform)
        {
            StatusMessage = $"目前選擇 {SelectedGitPlatform}，但網址屬於 {target.PlatformLabel}。請切換平台或更正網址。";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var site = _project.ProjectPath!;
            StatusMessage = $"正在準備 {target.PlatformLabel} repository 連線…";
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
                ? (target.Platform == GitHostingPlatform.GitHub ? PushCompletedStatusMessage(result) : $"已推送到 {target.PlatformLabel}")
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

        var requestedName = RepoName.Trim();
        var reuseExisting = false;
        var site = _project.ProjectPath!;
        IsBusy = true;
        try
        {
            var info = await _github.GetInfoAsync(site).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(info.Owner) && !string.IsNullOrWhiteSpace(info.Repo))
            {
                if (!info.Repo.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage =
                        $"本機已連結 {info.Owner}/{info.Repo}，與要建立的「{requestedName}」不同。Jekyller 不會改指向新 repository。" +
                        $"若這就是既有的 Jekyll 網站，請把名稱改成 {info.Repo}，或用上方「連結既有 GitHub Repository」。";
                    AppendLog(StatusMessage);
                    return;
                }

                RepositoryUrl = $"https://github.com/{info.Owner}/{info.Repo}";
                AppendLog($"本機已連結 {info.Owner}/{info.Repo}，改用安全連結既有 repository 流程。");
                reuseExisting = true;
            }
            else
            {
                StatusMessage = $"正在確認 GitHub 上是否已有 {requestedName}…";
                var lookup = await _github.LookupOwnedRepositoryAsync(requestedName).ConfigureAwait(true);
                if (!lookup.CheckSucceeded)
                {
                    StatusMessage = lookup.Message;
                    AppendLog(lookup.Message);
                    return;
                }

                if (lookup.Exists)
                {
                    AppendLog(lookup.Message);
                    if (!lookup.CanReuse || lookup.Target is not { IsValid: true })
                    {
                        StatusMessage = lookup.Message;
                        return;
                    }

                    RepositoryUrl = lookup.Target.CanonicalUrl ?? RepositoryUrl;
                    reuseExisting = true;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (reuseExisting)
        {
            await ConnectExistingRepositoryAsync().ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            var info = await _github.GetInfoAsync(site).ConfigureAwait(true);
            if (SyncRecommendedSiteUrls)
            {
                var target = GetSiteUrlTarget(info, requestedName);
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
                requestedName,
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
        var info = await _github.GetInfoAsync(site, SelectedGitPlatform).ConfigureAwait(true);
        var remoteTarget = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
        var configuredTarget = GitHubService.ParseRepositoryTarget(RepositoryUrl);
        var selectedTarget = GitPlatformSelection.GetSelectedPlatformTarget(
            SelectedGitPlatform,
            configuredTarget,
            remoteTarget);
        if (string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var candidate = !string.IsNullOrWhiteSpace(RepositoryUrl) ? RepositoryUrl : RepoName;
            var target = GitHubService.ParseRepositoryTarget(candidate);
            if (target.IsValid)
            {
                RepositoryUrl = candidate;
                AppendLog($"尚未設定 {GitHubService.RemoteNameFor(target.Platform)} remote；改用安全連結流程：{target.Owner}/{target.Repository}");
                await ConnectExistingRepositoryAsync().ConfigureAwait(true);
                return;
            }

            StatusMessage = "尚未連結 repository。請先在上方貼上完整 Repository URL，再按「連結並推送」。";
            AppendLog(StatusMessage);
            return;
        }

        if (selectedTarget.IsValid
            && remoteTarget.IsValid
            && !GitPlatformSelection.IsSameRepository(selectedTarget, remoteTarget))
        {
            StatusMessage =
                $"目前選取 {selectedTarget.PlatformLabel} ({selectedTarget.Owner}/{selectedTarget.Repository})，" +
                $"但本機 {info.RemoteName} remote 仍是 {remoteTarget.PlatformLabel} ({remoteTarget.Owner}/{remoteTarget.Repository})；已停止推送以避免送到錯的平台。請先按「連結並推送」或手動切換 {info.RemoteName} remote。";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            if (SyncRecommendedSiteUrls)
            {
                var target = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
                if (target.IsValid && !string.IsNullOrWhiteSpace(target.JekyllUrl))
                {
                    await _github.UpdateSiteUrlsAsync(site, target).ConfigureAwait(true);
                    AppendLog($"已依 {info.RemoteName} remote 同步 _config.yml：url={target.JekyllUrl}，baseurl={(string.IsNullOrEmpty(target.JekyllBaseUrl) ? "\"\"" : target.JekyllBaseUrl)}");
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
                SelectedGitPlatform,
                new Progress<string>(m =>
                {
                    AppendLog(m);
                    StatusMessage = m;
            })).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            var pushedTarget = GitHubService.ParseRepositoryTarget(info.RemoteUrl);
            StatusMessage = result.Success
                ? (pushedTarget.Platform == GitHostingPlatform.GitHub ? "推送完成；正在等待 Actions 部署確認" : $"已推送到 {pushedTarget.PlatformLabel}")
                : "推送失敗";
            if (!result.Success) return;

            if (pushedTarget.Platform == GitHostingPlatform.GitHub)
            {
                await RefreshPagesStatusAsync(updateStatusMessage: false).ConfigureAwait(true);
                await CheckDeploymentVersionAsync(manual: false, CancellationToken.None).ConfigureAwait(true);
            }
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
            AppendLog($"從 {GitHubService.RemoteNameFor(SelectedGitPlatform)} 拉取最新變更…");
            var result = await _github.PullAsync(_project.ProjectPath!, SelectedGitPlatform, new Progress<string>(AppendLog))
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
        if (!await EnsureGitHubRemoteAsync().ConfigureAwait(true)) return;
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

        var remote = await _github.DetectRemoteAsync(_project.ProjectPath!, SelectedGitPlatform).ConfigureAwait(true);
        var remoteTarget = GitHubService.ParseRepositoryTarget(remote);
        var configuredTarget = GitHubService.ParseRepositoryTarget(RepositoryUrl);
        var selectedTarget = GitPlatformSelection.GetSelectedPlatformTarget(
            SelectedGitPlatform,
            configuredTarget,
            remoteTarget);
        HostingPlatformLabel = selectedTarget.PlatformLabel;
        IsGitHubRemote = GitPlatformSelection.ShouldUseGitHubPagesApi(
            SelectedGitPlatform,
            selectedTarget,
            remoteTarget);
        var isLinkedToSelectedRepository = GitPlatformSelection.IsSameRepository(selectedTarget, remoteTarget);

        if (!isLinkedToSelectedRepository)
        {
            PagesUrl = string.Empty;
            PagesSummary = selectedTarget.IsValid && remoteTarget.IsValid
                ? $"目前選取 {selectedTarget.PlatformLabel}：{selectedTarget.Owner}/{selectedTarget.Repository}，但本機 {GitHubService.RemoteNameFor(SelectedGitPlatform)} remote 仍指向 {remoteTarget.PlatformLabel}：{remoteTarget.Owner}/{remoteTarget.Repository}。請先按「連結並推送」。"
                : $"目前選取 {selectedTarget.PlatformLabel}；請先填入並連結 repository。";
            DeploymentStatus = "尚未連結目前選取的平台";
            if (updateStatusMessage) StatusMessage = PagesSummary;
            return;
        }

        if (selectedTarget.IsValid && selectedTarget.Platform != GitHostingPlatform.GitHub)
        {
            PagesUrl = selectedTarget.PagesUrl ?? string.Empty;
            PagesSummary = NonGitHubPagesSummary(selectedTarget);
            DeploymentStatus = "非 GitHub Actions 部署";
            if (updateStatusMessage) StatusMessage = PagesSummary;
            return;
        }

        if (!IsGitHubRemote)
        {
            PagesUrl = string.Empty;
            PagesSummary = $"目前選取 {selectedTarget.PlatformLabel}；請先填入並連結 repository。";
            DeploymentStatus = "尚未連結目前選取的平台";
            if (updateStatusMessage) StatusMessage = PagesSummary;
            return;
        }

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
        if (!await EnsureGitHubRemoteAsync().ConfigureAwait(true)) return;
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
            var remote = await _github.DetectRemoteAsync(site, SelectedGitPlatform, cancellationToken).ConfigureAwait(false);
            var remoteTarget = GitHubService.ParseRepositoryTarget(remote);
            var configuredTarget = GitHubService.ParseRepositoryTarget(RepositoryUrl);
            var selectedTarget = GitPlatformSelection.GetSelectedPlatformTarget(
                SelectedGitPlatform,
                configuredTarget,
                remoteTarget);

            if (!GitPlatformSelection.IsSameRepository(selectedTarget, remoteTarget))
            {
                PagesUrl = string.Empty;
                DeploymentMonitorTitle = "尚未連結目前選取的平台";
                DeploymentMonitorSummary = selectedTarget.IsValid && remoteTarget.IsValid
                    ? $"目前選取 {selectedTarget.PlatformLabel}：{selectedTarget.Owner}/{selectedTarget.Repository}，但本機 {GitHubService.RemoteNameFor(SelectedGitPlatform)} remote 仍指向 {remoteTarget.PlatformLabel}：{remoteTarget.Owner}/{remoteTarget.Repository}。請先按「連結並推送」。"
                    : $"目前選取 {selectedTarget.PlatformLabel}；請先填入並連結 repository。";
                DeploymentMonitorSchedule = "連結目前選取的平台後開始檢查";
                _lastDeploymentState = null;
                _lastExpectedDeploymentId = null;
                return;
            }

            if (selectedTarget.IsValid && selectedTarget.Platform != GitHostingPlatform.GitHub)
            {
                PagesUrl = selectedTarget.PagesUrl ?? string.Empty;
                if (string.IsNullOrWhiteSpace(PagesUrl))
                {
                    DeploymentMonitorTitle = $"{selectedTarget.PlatformLabel} 線上版本監控尚未設定";
                    DeploymentMonitorSummary = $"{selectedTarget.PlatformLabel} 無法自動推定 Pages URL；請在平台端設定 Pages／CI。";
                    DeploymentMonitorSchedule = "設定 Pages URL 後再檢查";
                    return;
                }
            }
            else if (!GitPlatformSelection.ShouldUseGitHubPagesApi(SelectedGitPlatform, selectedTarget, remoteTarget))
            {
                PagesUrl = string.Empty;
                DeploymentMonitorTitle = "尚未連結目前選取的平台";
                DeploymentMonitorSummary = $"目前選取 {selectedTarget.PlatformLabel}，但本機 {GitHubService.RemoteNameFor(SelectedGitPlatform)} remote 尚未指向該 repository。";
                DeploymentMonitorSchedule = "連結目前選取的平台後開始檢查";
                return;
            }
            else if (string.IsNullOrWhiteSpace(PagesUrl))
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

    private static string BuildRemoteSummary(
        GitRemoteInfo info,
        GitHubRepositoryTarget selectedTarget,
        GitHubRepositoryTarget remoteTarget)
    {
        var lines = new List<string>
        {
            $"目前選取：{FormatRepositoryTarget(selectedTarget)}",
            $"本機 {info.RemoteName} remote：{FormatRepositoryRemote(info.RemoteUrl, remoteTarget, info.RemoteName)}",
            $"分支：{info.Branch ?? "—"}"
        };

        if (selectedTarget.Platform == GitHostingPlatform.GitHub)
        {
            lines.Add($"GitHub 使用者：{info.GhUser ?? "（未登入）"}");
            lines.Add($"GitHub 驗證：{(info.GhAuthenticated ? "已登入" : "未登入")}");
        }

        if (selectedTarget.IsValid
            && remoteTarget.IsValid
            && !GitPlatformSelection.IsSameRepository(selectedTarget, remoteTarget))
        {
            lines.Add(
                $"狀態：本機 {info.RemoteName} remote 仍指向 {remoteTarget.PlatformLabel}；目前選取的是 {selectedTarget.PlatformLabel}。請先「連結並推送」，才會更新目前平台的 remote。");
        }
        else if (selectedTarget.IsValid && !remoteTarget.IsValid)
        {
            lines.Add($"狀態：目前選取的 repository 尚未連結為本機 {info.RemoteName} remote。");
        }
        else if (selectedTarget.IsValid)
        {
            lines.Add($"狀態：本機 {info.RemoteName} remote 與目前選取的 repository 一致。");
        }
        else if (remoteTarget.IsValid)
        {
            lines.Add($"狀態：請填入 {selectedTarget.PlatformLabel} repository URL；本機 {info.RemoteName} remote 目前是 {remoteTarget.PlatformLabel}。");
        }
        else
        {
            lines.Add("狀態：尚未連結 repository。");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatRepositoryTarget(GitHubRepositoryTarget target) =>
        target.IsValid
            ? $"{target.PlatformLabel}：{target.Owner}/{target.Repository}"
            : $"{target.PlatformLabel}：尚未填入 repository URL";

    private static string FormatRepositoryRemote(string? remoteUrl, GitHubRepositoryTarget remoteTarget, string remoteName) =>
        remoteTarget.IsValid
            ? $"{remoteTarget.PlatformLabel}：{remoteTarget.Owner}/{remoteTarget.Repository} ({remoteUrl})"
            : string.IsNullOrWhiteSpace(remoteUrl) ? $"（無 {remoteName} remote）" : remoteUrl;

    private void UpdateLocalRepositorySummary(GitHubRepositoryTarget configuredTarget)
    {
        if (!_project.HasProject || _lastRemoteInfo is null)
            return;

        var remoteTarget = GitHubService.ParseRepositoryTarget(_lastRemoteInfo.RemoteUrl);
        var selectedTarget = GitPlatformSelection.GetSelectedPlatformTarget(
            SelectedGitPlatform,
            configuredTarget,
            remoteTarget);

        HostingPlatformLabel = selectedTarget.PlatformLabel;
        IsGitHubRemote = GitPlatformSelection.ShouldUseGitHubPagesApi(
            SelectedGitPlatform,
            selectedTarget,
            remoteTarget);
        RemoteSummary = BuildRemoteSummary(_lastRemoteInfo, selectedTarget, remoteTarget);

        if (selectedTarget.IsValid && selectedTarget.Platform != GitHostingPlatform.GitHub)
        {
            PagesUrl = selectedTarget.PagesUrl ?? string.Empty;
            PagesSummary = NonGitHubPagesSummary(selectedTarget);
            DeploymentStatus = "非 GitHub Actions 部署";
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

    partial void OnCloneParentDirectoryChanged(string value) => UpdateCloneTargetSummary();

    partial void OnCloneSiteNameChanged(string value) => UpdateCloneTargetSummary();

    private async Task LoadPagesSitesCoreAsync()
    {
        PagesSites.Clear();
        HasPagesSites = false;
        PagesSitesMessage = "正在查詢 GitHub Pages 網站…";
        var result = await _github.ListPagesSitesAsync().ConfigureAwait(true);
        PagesSitesMessage = result.Message;
        if (!result.Success)
        {
            AppendLog(result.Message);
            return;
        }

        foreach (var site in result.Sites)
            PagesSites.Add(site);

        HasPagesSites = PagesSites.Count > 0;
        if (HasPagesSites)
            AppendLog($"找到 {PagesSites.Count} 個 GitHub Pages 網站。");
    }

    private async Task FinishLocalCloneAsync(string dest, IProgress<string> progress)
    {
        if (File.Exists(Path.Combine(dest, "Gemfile")))
        {
            progress.Report("偵測到 Gemfile，執行 bundle install…");
            var bundle = await _jekyll.BundleInstallAsync(dest, progress).ConfigureAwait(true);
            AppendLog(bundle.Success ? "bundle install 完成。" : "bundle install 失敗：\n" + bundle.CombinedOutput);
            if (!bundle.Success)
                progress.Report("網站已複製，但 bundle install 失敗。可稍後在「環境建立」再執行。");
        }

        if (!_jekyll.LooksLikeJekyllSite(dest))
        {
            AppendLog("已複製，但資料夾看起來不像 Jekyll 來源（缺少 _config.yml / Gemfile / _posts）。可能遠端是編譯後的 Pages 內容。");
            await _dialogs.ShowMessageAsync(
                "已複製，但可能不是 Jekyll 來源",
                "資料夾已複製到本機並開啟，但沒有找到典型的 Jekyll 檔案。若遠端只放了編譯後的 HTML，請改複製含 _config.yml 的來源分支。")
                .ConfigureAwait(true);
        }

        _project.SetProject(dest);
    }

    private void UpdateCloneTargetSummary()
    {
        var target = GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!target.IsValid)
        {
            CloneTargetSummary = string.IsNullOrWhiteSpace(RepositoryUrl)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址，或從下方清單選擇已上線的 GitHub 網站。"
                : target.ErrorMessage;
            return;
        }

        if (target.Platform != SelectedGitPlatform)
        {
            CloneTargetSummary = $"目前選擇 {SelectedGitPlatform}，但網址屬於 {target.PlatformLabel}。請切換平台或更正網址。";
            return;
        }

        if (!GitHubCloneDestination.TryCreatePath(CloneParentDirectory, CloneSiteName, out var dest, out var error))
        {
            CloneTargetSummary = error;
            return;
        }

        CloneTargetSummary =
            $"將複製 {target.Owner}/{target.Repository}" +
            (string.IsNullOrWhiteSpace(target.PagesUrl) ? string.Empty : $"（{target.PagesUrl}）") +
            $"\n到本機：{dest}";
    }

    private bool EnsureProject()
    {
        if (_project.HasProject) return true;
        _ = _dialogs.ShowMessageAsync("提示", "請先開啟、建立專案，或從 Git 平台複製到本機。");
        return false;
    }

    private async Task<bool> EnsureGitHubRemoteAsync()
    {
        var remote = await _github.DetectRemoteAsync(_project.ProjectPath!).ConfigureAwait(true);
        var target = GitHubService.ParseRepositoryTarget(remote);
        if (target.IsValid && target.Platform == GitHostingPlatform.GitHub)
            return true;

        var platform = target.IsValid ? target.PlatformLabel : "目前";
        StatusMessage = $"{platform} repository 不使用 GitHub Actions／Pages API；請使用該平台的 CI／Pages 設定。";
        AppendLog(StatusMessage);
        return false;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {line.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? formatted : Log + Environment.NewLine + formatted;
    }

    private static string NonGitHubPagesSummary(GitHubRepositoryTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.PagesUrl))
        {
            return target.Platform == GitHostingPlatform.Bitbucket
                ? "Bitbucket Cloud 只支援 <workspace>.bitbucket.io 靜態網站。請改連該 repository。"
                : $"{target.PlatformLabel} repository 已選取；Pages／CI 部署需在平台端設定。";
        }

        return target.Platform switch
        {
            GitHostingPlatform.GitLab =>
                $"{target.PlatformLabel} 建議網站網址：{target.PagesUrl}（推送後由 GitLab Pages CI 建置；" +
                "Settings > Pages 若是 Everyone With Access 需登入，公開請改 Everyone；快取通常不到 1 分鐘）",
            GitHostingPlatform.Codeberg =>
                $"{target.PlatformLabel} 建議網站網址：{target.PagesUrl}（會把 _site 推到 pages 分支；請在 Codeberg 設定 Pages Webhook）",
            GitHostingPlatform.Bitbucket =>
                $"{target.PlatformLabel} 建議網站網址：{target.PagesUrl}（會把 _site 推到預設分支）",
            _ => $"{target.PlatformLabel} 建議網站網址：{target.PagesUrl}"
        };
    }

    private static string PushCompletedStatusMessage(ProcessResult result)
    {
        return result.CombinedOutput.Contains("沒有管理 GitHub Pages 設定所需的 admin 權限", StringComparison.OrdinalIgnoreCase)
            ? "網站檔案已推送；目前帳號無法自動啟用 Pages，請由 Repository 擁有者在 Settings > Pages 將 Source 設為 GitHub Actions。"
            : "已推送並已請求啟用 GitHub Pages；正在等待 Actions 部署確認";
    }
}
