using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private readonly IJekyllService _jekyll;

    [ObservableProperty]
    public partial string ProjectPath { get; set; } = "尚未選擇專案";

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = "歡迎使用 Jekyller：一鍵建立 Jekyll、編輯設定、安裝主題、撰寫 Markdown，並部署到 GitHub Pages。";

    [ObservableProperty]
    public partial bool HasProject { get; set; }

    public HomeViewModel(IProjectContext project, IDialogService dialogs, IJekyllService jekyll)
    {
        _project = project;
        _dialogs = dialogs;
        _jekyll = jekyll;
        _project.ProjectChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        HasProject = _project.HasProject;
        ProjectPath = _project.HasProject ? _project.ProjectPath! : "尚未選擇專案";
        StatusSummary = _project.HasProject
            ? $"目前專案：{_project.ProjectPath}"
            : "請先在「環境建立」建立新站，或開啟既有 Jekyll 專案資料夾。";
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
    private void CloseProject()
    {
        _project.SetProject(null);
    }
}
