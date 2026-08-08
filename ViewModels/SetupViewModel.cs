using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class SetupViewModel : ViewModelBase
{
    private readonly IEnvironmentService _environment;
    private readonly IJekyllService _jekyll;
    private readonly IDialogService _dialogs;
    private readonly IProjectContext _project;

    public ObservableCollection<ToolStatus> Tools { get; } = [];

    [ObservableProperty]
    public partial string ParentDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial string SiteName { get; set; } = "my-jekyll-site";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProjectPathDisplay { get; set; } = "尚未開啟專案";

    public SetupViewModel(
        IEnvironmentService environment,
        IJekyllService jekyll,
        IDialogService dialogs,
        IProjectContext project)
    {
        _environment = environment;
        _jekyll = jekyll;
        _dialogs = dialogs;
        _project = project;
        _project.ProjectChanged += (_, _) =>
            ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
        ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
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
    private async Task OpenExistingAsync()
    {
        var folder = await _dialogs.PickFolderAsync("開啟既有 Jekyll 專案").ConfigureAwait(true);
        if (folder is null) return;
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

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
    }
}
