using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Helpers;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class SetupViewModel : ViewModelBase
{
    private readonly IEnvironmentService _environment;
    private readonly IJekyllService _jekyll;
    private readonly IDialogService _dialogs;
    private readonly IProjectContext _project;
    private readonly IGitHubService _github;
    private readonly ISettingsService _settings;
    private bool _changingPlatform;
    private string? _lastAutoCloneSiteName;

    public ObservableCollection<ToolStatus> Tools { get; } = [];
    public IReadOnlyList<GitHostingPlatform> GitPlatforms { get; } = Enum.GetValues<GitHostingPlatform>();

    [ObservableProperty]
    public partial GitHostingPlatform SelectedGitPlatform { get; set; } = GitHostingPlatform.GitHub;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string ParentDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial string SiteName { get; set; } = "my-jekyll-site";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProjectPathDisplay { get; set; } = "尚未開啟專案";

    [ObservableProperty]
    public partial string JekyllVersionMessage { get; set; } = "正在檢查 Jekyll 最新版本…";

    [ObservableProperty]
    public partial bool IsJekyllUpdateAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string CloneRepositoryUrl { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial string CloneSiteName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloneTargetSummary { get; set; } = "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository 網址。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneNow))]
    public partial bool IsBusy { get; set; }

    public bool CanCloneNow =>
        !IsBusy
        && GitHubService.ParseRepositoryTarget(CloneRepositoryUrl).IsValid
        && GitHubService.ParseRepositoryTarget(CloneRepositoryUrl).Platform == SelectedGitPlatform
        && !string.IsNullOrWhiteSpace(ParentDirectory)
        && !string.IsNullOrWhiteSpace(CloneSiteName);

    public SetupViewModel(
        IEnvironmentService environment,
        IJekyllService jekyll,
        IDialogService dialogs,
        IProjectContext project,
        IGitHubService github,
        ISettingsService settings)
    {
        _environment = environment;
        _jekyll = jekyll;
        _dialogs = dialogs;
        _project = project;
        _github = github;
        _settings = settings;
        if (Enum.TryParse<GitHostingPlatform>(_settings.Current.SelectedGitPlatform, true, out var platform))
            SelectedGitPlatform = platform;
        _changingPlatform = true;
        CloneRepositoryUrl = _settings.GetRepositoryUrl(SelectedGitPlatform.ToString());
        _changingPlatform = false;
        _project.ProjectChanged += (_, _) =>
            ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
        ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
        _ = RefreshJekyllVersionAsync(writeLog: false);
    }

    [RelayCommand]
    private async Task CheckEnvironmentAsync()
    {
        IsBusy = true;
        AppendLog("正在檢查本機工具…");
        try
        {
            var tools = await _environment.CheckToolsAsync().ConfigureAwait(true);
            Tools.Clear();
            foreach (var t in tools)
                Tools.Add(t);
            await RefreshJekyllVersionAsync(writeLog: false).ConfigureAwait(true);
            AppendLog("環境檢查完成。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallJekyllAsync()
    {
        IsBusy = true;
        try
        {
            AppendLog("安裝 jekyll / bundler（gem install）…");
            var progress = new Progress<string>(AppendLog);
            var result = await _environment.InstallJekyllAsync(progress).ConfigureAwait(true);
            AppendLog(result.Success ? "安裝完成。" : "安裝失敗：\n" + result.CombinedOutput);
            await CheckEnvironmentAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CheckJekyllVersionAsync() => RefreshJekyllVersionAsync(writeLog: true);

    private async Task RefreshJekyllVersionAsync(bool writeLog)
    {
        JekyllVersionMessage = "正在向 RubyGems 檢查 Jekyll 最新版本…";
        try
        {
            var result = await _environment.CheckJekyllVersionAsync().ConfigureAwait(true);
            IsJekyllUpdateAvailable = result.IsUpdateAvailable;
            JekyllVersionMessage = result.Message;
            if (writeLog)
                AppendLog(result.Message);
        }
        catch (Exception ex)
        {
            IsJekyllUpdateAvailable = false;
            JekyllVersionMessage = $"暫時無法檢查 Jekyll 最新版本：{ex.Message}";
            if (writeLog)
                AppendLog(JekyllVersionMessage);
        }
    }

    [RelayCommand]
    private async Task BrowseParentAsync()
    {
        var folder = await _dialogs.PickFolderAsync("選擇新站建立位置").ConfigureAwait(true);
        if (folder is not null)
            ParentDirectory = folder;
    }

    [RelayCommand]
    private async Task CreateSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(ParentDirectory) || string.IsNullOrWhiteSpace(SiteName))
        {
            await _dialogs.ShowMessageAsync("提示", "請填寫父資料夾與網站名稱。").ConfigureAwait(true);
            return;
        }

        var target = Path.Combine(ParentDirectory, SiteName);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            var ok = await _dialogs.ConfirmAsync("資料夾已存在", "目標資料夾非空，要以 --force 建立嗎？").ConfigureAwait(true);
            if (!ok) return;
        }

        IsBusy = true;
        try
        {
            AppendLog($"建立 Jekyll 站台：{target}");
            var progress = new Progress<string>(AppendLog);
            var force = Directory.Exists(target);
            var result = await _jekyll.CreateSiteAsync(ParentDirectory, SiteName, force, progress).ConfigureAwait(true);
            if (result.Success)
            {
                AppendLog("站台建立成功，已自動 bundle install。");
                _project.SetProject(target);
            }
            else
            {
                AppendLog("建立失敗：\n" + result.CombinedOutput);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloneFromGitHubAsync()
    {
        var target = GitHubService.ParseRepositoryTarget(CloneRepositoryUrl);
        if (!target.IsValid)
        {
            await _dialogs.ShowMessageAsync("提示", target.ErrorMessage).ConfigureAwait(true);
            return;
        }

        if (!GitHubCloneDestination.TryCreatePath(ParentDirectory, CloneSiteName, out var dest, out var pathError))
        {
            await _dialogs.ShowMessageAsync("無法複製", pathError).ConfigureAwait(true);
            return;
        }

        if (!GitHubCloneDestination.IsVacant(dest))
        {
            await _dialogs.ShowMessageAsync(
                "無法複製",
                $"目標資料夾不是空的：{dest}。請換一個資料夾名稱，以免覆蓋現有檔案。").ConfigureAwait(true);
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
            AppendLog($"從 {target.PlatformLabel} 複製 {target.Owner}/{target.Repository} 到 {dest}");
            var progress = new Progress<string>(AppendLog);
            var result = await _github.CloneRepositoryAsync(target, dest, progress).ConfigureAwait(true);
            AppendLog(result.CombinedOutput);
            if (!result.Success)
            {
                AppendLog("複製失敗。");
                return;
            }

            if (File.Exists(Path.Combine(dest, "Gemfile")))
            {
                AppendLog("偵測到 Gemfile，執行 bundle install…");
                var bundle = await _jekyll.BundleInstallAsync(dest, progress).ConfigureAwait(true);
                AppendLog(bundle.Success ? "bundle install 完成。" : "bundle install 失敗：\n" + bundle.CombinedOutput);
            }

            if (!_jekyll.LooksLikeJekyllSite(dest))
            {
                AppendLog("已複製，但資料夾看起來不像 Jekyll 來源（缺少 _config.yml / Gemfile / _posts）。");
                await _dialogs.ShowMessageAsync(
                    "已複製，但可能不是 Jekyll 來源",
                    "資料夾已複製到本機並開啟，但沒有找到典型的 Jekyll 檔案。若 GitHub Pages 只放了編譯後的 HTML，請改複製含 _config.yml 的來源分支。")
                    .ConfigureAwait(true);
            }

            _project.SetProject(dest);
            AppendLog("已開啟複製下來的專案：" + dest);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenExistingAsync()
    {
        var folder = await _dialogs.PickFolderAsync("開啟既有 Jekyll 專案").ConfigureAwait(true);
        if (folder is null) return;

        if (!_jekyll.LooksLikeJekyllSite(folder))
        {
            var ok = await _dialogs.ConfirmAsync(
                "看起來不像 Jekyll 專案",
                "此資料夾未找到 _config.yml / _posts / Gemfile。仍要開啟嗎？").ConfigureAwait(true);
            if (!ok) return;
        }

        _project.SetProject(folder);
        AppendLog("已開啟專案：" + folder);
    }

    [RelayCommand]
    private async Task BundleInstallAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟或建立專案。").ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            AppendLog("執行 bundle install…");
            var progress = new Progress<string>(AppendLog);
            var result = await _jekyll.BundleInstallAsync(_project.ProjectPath!, progress).ConfigureAwait(true);
            AppendLog(result.Success ? "bundle install 完成。" : "失敗：\n" + result.CombinedOutput);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟或建立專案。").ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            AppendLog("執行 jekyll build…");
            var progress = new Progress<string>(AppendLog);
            var result = await _jekyll.BuildAsync(_project.ProjectPath!, progress).ConfigureAwait(true);
            AppendLog(result.Success ? "建置完成（_site）。" : "建置失敗：\n" + result.CombinedOutput);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCloneRepositoryUrlChanged(string value)
    {
        if (!_changingPlatform)
            _settings.SetRepositoryUrl(SelectedGitPlatform.ToString(), value);
        var target = GitHubService.ParseRepositoryTarget(value);
        if (target.IsValid && !string.IsNullOrWhiteSpace(target.Repository))
        {
            if (string.IsNullOrWhiteSpace(CloneSiteName) || CloneSiteName == _lastAutoCloneSiteName)
                CloneSiteName = target.Repository;
            _lastAutoCloneSiteName = target.Repository;
        }

        UpdateCloneTargetSummary();
    }

    partial void OnSelectedGitPlatformChanged(GitHostingPlatform value)
    {
        _settings.SetSelectedGitPlatform(value.ToString());
        _changingPlatform = true;
        CloneRepositoryUrl = _settings.GetRepositoryUrl(value.ToString());
        _changingPlatform = false;
        UpdateCloneTargetSummary();
    }

    partial void OnCloneSiteNameChanged(string value) => UpdateCloneTargetSummary();

    partial void OnParentDirectoryChanged(string value) => UpdateCloneTargetSummary();

    private void UpdateCloneTargetSummary()
    {
        var target = GitHubService.ParseRepositoryTarget(CloneRepositoryUrl);
        if (!target.IsValid)
        {
            CloneTargetSummary = string.IsNullOrWhiteSpace(CloneRepositoryUrl)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository 網址。"
                : target.ErrorMessage;
            return;
        }

        if (target.Platform != SelectedGitPlatform)
        {
            CloneTargetSummary = $"目前選擇 {SelectedGitPlatform}，但網址屬於 {target.PlatformLabel}。請切換平台或更正網址。";
            return;
        }

        if (!GitHubCloneDestination.TryCreatePath(ParentDirectory, CloneSiteName, out var dest, out var error))
        {
            CloneTargetSummary = error;
            return;
        }

        CloneTargetSummary = $"將從 {target.PlatformLabel} 複製 {target.Owner}/{target.Repository} 到 {dest}";
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
    }
}
