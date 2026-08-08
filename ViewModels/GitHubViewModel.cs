using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class GitHubViewModel : ViewModelBase
{
    private readonly IGitHubService _github;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    public partial string RepoName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPrivate { get; set; }

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "Update site via Jekyller";

    [ObservableProperty]
    public partial string PagesBranch { get; set; } = "main";

    [ObservableProperty]
    public partial string PagesPath { get; set; } = "/";

    [ObservableProperty]
    public partial string RemoteUrl { get; set; } = "—";

    [ObservableProperty]
    public partial string PagesStatusText { get; set; } = "尚未查詢";

    [ObservableProperty]
    public partial string PagesUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public GitHubViewModel(IGitHubService github, IProjectContext project, IDialogService dialogs)
    {
        _github = github;
        _project = project;
        _dialogs = dialogs;
        _project.ProjectChanged += async (_, _) => await RefreshRemoteAsync().ConfigureAwait(true);
        _ = RefreshRemoteAsync();
    }

    [RelayCommand]
    private async Task RefreshRemoteAsync()
    {
        if (!_project.HasProject)
        {
            RemoteUrl = "尚未開啟專案";
            RepoName = string.Empty;
            return;
        }

        var remote = await _github.DetectRemoteAsync(_project.ProjectPath!).ConfigureAwait(true);
        RemoteUrl = remote ?? "（尚無 origin remote）";

        if (string.IsNullOrWhiteSpace(RepoName))
            RepoName = Path.GetFileName(_project.ProjectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    [RelayCommand]
    private async Task InitGitAsync()
    {
        if (!EnsureProject()) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(AppendLog);
            var result = await _github.InitRepositoryAsync(_project.ProjectPath!, progress).ConfigureAwait(true);
            AppendLog(result.Success ? "Git 初始化完成。" : result.CombinedOutput);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAndPushAsync()
    {
        if (!EnsureProject()) return;
        if (string.IsNullOrWhiteSpace(RepoName))
        {
            await _dialogs.ShowMessageAsync("提示", "請輸入 repository 名稱。").ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            AppendLog($"建立 GitHub repo 並推送：{RepoName}…");
            var progress = new Progress<string>(AppendLog);
            var result = await _github.CreateAndPushAsync(
                _project.ProjectPath!,
                RepoName.Trim(),
                IsPrivate,
                CommitMessage,
                progress).ConfigureAwait(true);

            AppendLog(result.Success ? "推送完成。" : "失敗：\n" + result.CombinedOutput);
            await RefreshRemoteAsync().ConfigureAwait(true);
            if (result.Success)
                await CheckPagesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CommitPushAsync()
    {
        if (!EnsureProject()) return;
        IsBusy = true;
        try
        {
            AppendLog("提交並推送變更…");
            var progress = new Progress<string>(AppendLog);
            var result = await _github.CommitAndPushAsync(_project.ProjectPath!, CommitMessage, progress)
                .ConfigureAwait(true);
            AppendLog(result.Success ? "推送完成。" : "失敗：\n" + result.CombinedOutput);
            await RefreshRemoteAsync().ConfigureAwait(true);
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
            AppendLog($"啟用 GitHub Pages（{PagesBranch} {PagesPath}）…");
            var progress = new Progress<string>(AppendLog);
            var result = await _github.EnablePagesAsync(_project.ProjectPath!, PagesBranch, PagesPath, progress)
                .ConfigureAwait(true);
            AppendLog(result.Success ? "已送出啟用請求。" : "啟用失敗（可能已啟用）：\n" + result.CombinedOutput);
            await CheckPagesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckPagesAsync()
    {
        if (!EnsureProject()) return;
        IsBusy = true;
        try
        {
            AppendLog("查詢 GitHub Pages 狀態…");
            var status = await _github.GetPagesStatusAsync(_project.ProjectPath!).ConfigureAwait(true);
            if (!status.Success)
            {
                PagesStatusText = status.Message;
                PagesUrl = string.Empty;
                AppendLog(status.Message);
                return;
            }

            if (!status.IsEnabled)
            {
                PagesStatusText = status.Message;
                PagesUrl = string.Empty;
            }
            else
            {
                PagesStatusText =
                    $"狀態：{status.Status}\n來源：{status.SourceBranch} {status.SourcePath}\nCNAME：{(string.IsNullOrEmpty(status.Cname) ? "（無）" : status.Cname)}";
                PagesUrl = status.HtmlUrl;
            }

            AppendLog(status.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
    }
}
