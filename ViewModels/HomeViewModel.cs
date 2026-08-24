using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private readonly IJekyllService _jekyll;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    public partial string ProjectPath { get; set; } = "尚未選擇專案";

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = "歡迎使用 Jekyller：一鍵建立 Jekyll、編輯內容，並同步到 GitHub、GitLab、Codeberg 或 Bitbucket。";

    [ObservableProperty]
    public partial bool HasProject { get; set; }

    [ObservableProperty]
    public partial bool HasRecentProjects { get; set; }

    [ObservableProperty]
    public partial bool AutoOpenLastProject { get; set; }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public HomeViewModel(
        IProjectContext project,
        IDialogService dialogs,
        IJekyllService jekyll,
        ISettingsService settings)
    {
        _project = project;
        _dialogs = dialogs;
        _jekyll = jekyll;
        _settings = settings;
        AutoOpenLastProject = _settings.IsAutoOpenLastProjectEnabled;
        _project.ProjectChanged += (_, _) => Refresh();
        Refresh();
    }

    partial void OnAutoOpenLastProjectChanged(bool value) =>
        _settings.SetAutoOpenLastProject(value);

    private void Refresh()
    {
        HasProject = _project.HasProject;
        ProjectPath = _project.HasProject ? _project.ProjectPath! : "尚未選擇專案";
        StatusSummary = _project.HasProject
            ? $"目前專案：{_project.ProjectPath}"
            : "請先在「環境建立」建立新站、開啟既有專案，或到「GitHub Pages」把線上網站複製到本機。";
        RefreshRecents();
    }

    private void RefreshRecents()
    {
        RecentProjects.Clear();
        foreach (var path in _settings.GetExistingRecentProjects())
        {
            var isCurrent = _project.HasProject
                && string.Equals(path, _project.ProjectPath, StringComparison.OrdinalIgnoreCase);
            RecentProjects.Add(new RecentProjectItem(path, isCurrent));
        }

        HasRecentProjects = RecentProjects.Count > 0;
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var folder = await _dialogs.PickFolderAsync("選擇 Jekyll 專案資料夾").ConfigureAwait(true);
        if (folder is null) return;

        if (!_jekyll.LooksLikeJekyllSite(folder))
        {
            var ok = await _dialogs.ConfirmAsync(
                "看起來不像 Jekyll 專案",
                "此資料夾未找到 _config.yml / _posts / Gemfile。仍要開啟嗎？").ConfigureAwait(true);
            if (!ok) return;
        }

        _project.SetProject(folder);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!Directory.Exists(path))
        {
            _settings.RemoveRecentProject(path);
            RefreshRecents();
            await _dialogs.ShowMessageAsync("找不到專案", "資料夾不存在，已從最近清單移除。").ConfigureAwait(true);
            return;
        }

        _project.SetProject(path);
    }

    [RelayCommand]
    private void RemoveRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _settings.RemoveRecentProject(path);
        RefreshRecents();
    }

    [RelayCommand]
    private void CloseProject()
    {
        _project.SetProject(null);
    }
}
