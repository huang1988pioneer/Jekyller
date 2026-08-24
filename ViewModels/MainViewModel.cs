using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IProjectContext _project;

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    public partial NavItem? SelectedNav { get; set; }

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string ProjectBadge { get; set; } = "未開啟專案";

    public MainViewModel(
        IProjectContext project,
        HomeViewModel home,
        SetupViewModel setup,
        ConfigViewModel config,
        ThemeViewModel theme,
        ContentViewModel content,
        PreviewViewModel preview,
        GitHubViewModel github)
    {
        _project = project;

        NavItems.Add(new NavItem("首頁", "🏠", home));
        NavItems.Add(new NavItem("環境建立", "⚙️", setup));
        NavItems.Add(new NavItem("站台設定", "📝", config));
        NavItems.Add(new NavItem("主題 Themes", "🎨", theme));
        NavItems.Add(new NavItem("內容 Markdown", "📄", content));
        NavItems.Add(new NavItem("本機預覽", "👁", preview));
        NavItems.Add(new NavItem("Git 平台 / Pages", "🚀", github));

        SelectedNav = NavItems[0];
        CurrentPage = SelectedNav.ViewModel;
        SelectedNav.IsSelected = true;

        _project.ProjectChanged += (_, _) => UpdateBadge();
        UpdateBadge();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;

        foreach (var item in NavItems)
            item.IsSelected = item == value;

        CurrentPage = value.ViewModel;
        if (value.ViewModel is ContentViewModel content
            && content.RefreshCommand.CanExecute(null))
            content.RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        SelectedNav = item;
    }

    private void UpdateBadge()
    {
        ProjectBadge = _project.HasProject
            ? Path.GetFileName(_project.ProjectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : "未開啟專案";
    }
}
