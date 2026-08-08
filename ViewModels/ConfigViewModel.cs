using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BaseUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Theme { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RemoteTheme { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Lang { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Timezone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Markdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Permalink { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RawYaml { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "開啟專案後可載入 _config.yml";

    [ObservableProperty]
    public partial bool HasProject { get; set; }

    public ConfigViewModel(IConfigService config, IProjectContext project, IDialogService dialogs)
    {
        _config = config;
        _project = project;
        _dialogs = dialogs;
        _project.ProjectChanged += async (_, _) => await LoadAsync().ConfigureAwait(true);
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        HasProject = _project.HasProject;
        if (!_project.HasProject)
        {
            StatusMessage = "尚未開啟專案。";
            return;
        }

        try
        {
            var snap = await _config.LoadAsync(_project.ProjectPath!).ConfigureAwait(true);
            Title = snap.Title;
            Description = snap.Description;
            Url = snap.Url;
            BaseUrl = snap.BaseUrl;
            Email = snap.Email;
            Theme = snap.Theme;
            RemoteTheme = snap.RemoteTheme;
            Lang = snap.Lang;
            Timezone = snap.Timezone;
            Markdown = snap.Markdown;
            Permalink = snap.Permalink;
            RawYaml = snap.RawYaml;
            StatusMessage = "已載入 " + (_config.FindConfigPath(_project.ProjectPath!) ?? "_config.yml");
        }
        catch (Exception ex)
        {
            StatusMessage = "載入失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveFieldsAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            await _config.SaveFieldsAsync(_project.ProjectPath!, new JekyllConfigSnapshot
            {
                Title = Title,
                Description = Description,
                Url = Url,
                BaseUrl = BaseUrl,
                Email = Email,
                Theme = Theme,
                RemoteTheme = RemoteTheme,
                Lang = Lang,
                Timezone = Timezone,
                Markdown = Markdown,
                Permalink = Permalink
            }).ConfigureAwait(true);

            StatusMessage = "已儲存常用欄位到 _config.yml";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = "儲存失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveRawAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            await _config.SaveRawAsync(_project.ProjectPath!, RawYaml).ConfigureAwait(true);
            StatusMessage = "已儲存原始 YAML";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = "儲存失敗：" + ex.Message;
        }
    }
}
