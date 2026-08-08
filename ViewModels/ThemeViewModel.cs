using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class ThemeViewModel : ViewModelBase
{
    private readonly IThemeService _themes;
    private readonly IConfigService _config;
    private readonly IChirpyConfigService _chirpy;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;

    public ObservableCollection<ThemeInfo> Themes { get; } = [];
    public ObservableCollection<string> ThemeModeOptions { get; } = ["", "light", "dark"];
    public ObservableCollection<string> CommentProviderOptions { get; } =
        ["", "disqus", "utterances", "giscus"];

    [ObservableProperty]
    public partial ThemeInfo? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string CurrentTheme { get; set; } = "—";

    [ObservableProperty]
    public partial string ThemeConfigYaml { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ThemeConfigPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    // —— Chirpy form fields ——
    [ObservableProperty] public partial string ChirpyTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyTagline { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyBaseUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyLang { get; set; } = "en";
    [ObservableProperty] public partial string ChirpyTimezone { get; set; } = "Asia/Taipei";
    [ObservableProperty] public partial string ChirpyThemeMode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyAvatar { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyCdn { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpySocialPreviewImage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ChirpyToc { get; set; } = true;
    [ObservableProperty] public partial int ChirpyPaginate { get; set; } = 10;
    [ObservableProperty] public partial string ChirpyGithubUsername { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyTwitterUsername { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpySocialName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpySocialEmail { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyFediverseHandle { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpySocialLinks { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGoogleVerification { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyBingVerification { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGoogleAnalyticsId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGoatCounterId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyCommentsProvider { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyDisqusShortname { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyUtterancesRepo { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyUtterancesIssueTerm { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGiscusRepo { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGiscusRepoId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGiscusCategory { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChirpyGiscusCategoryId { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ChirpyPwaEnabled { get; set; } = true;
    [ObservableProperty] public partial bool ChirpyPwaCacheEnabled { get; set; } = true;
    [ObservableProperty] public partial bool ChirpyAssetsSelfHostEnabled { get; set; }
    [ObservableProperty] public partial bool ChirpyEditPostEnabled { get; set; }
    [ObservableProperty] public partial string ChirpyEditPostUrl { get; set; } = string.Empty;

    public ThemeViewModel(
        IThemeService themes,
        IConfigService config,
        IChirpyConfigService chirpy,
        IProjectContext project,
        IDialogService dialogs)
    {
        _themes = themes;
        _config = config;
        _chirpy = chirpy;
        _project = project;
        _dialogs = dialogs;

        foreach (var t in _themes.GetCatalog())
            Themes.Add(t);

        SelectedTheme = Themes.FirstOrDefault(t => t.Id == "chirpy") ?? Themes.FirstOrDefault();
        _project.ProjectChanged += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_project.HasProject)
        {
            CurrentTheme = "尚未開啟專案";
            ThemeConfigYaml = string.Empty;
            ThemeConfigPath = string.Empty;
            StatusMessage = "請先開啟專案以編輯 Chirpy 設定。";
            return;
        }

        CurrentTheme = await _themes.DetectCurrentThemeAsync(_project.ProjectPath!).ConfigureAwait(true);
        ThemeConfigPath = _config.FindThemeConfigPath(_project.ProjectPath!) ?? "（無）";
        ThemeConfigYaml = await _config.LoadThemeConfigAsync(_project.ProjectPath!).ConfigureAwait(true) ?? string.Empty;

        try
        {
            var s = await _chirpy.LoadAsync(_project.ProjectPath!).ConfigureAwait(true);
            ApplyChirpyToForm(s);
            StatusMessage = "已載入 Chirpy / 站台設定表單。";
        }
        catch (Exception ex)
        {
            StatusMessage = "載入表單失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟或建立專案。").ConfigureAwait(true);
            return;
        }

        if (SelectedTheme is null)
        {
            await _dialogs.ShowMessageAsync("提示", "請選擇主題。").ConfigureAwait(true);
            return;
        }

        var ok = await _dialogs.ConfirmAsync(
            "安裝主題",
            $"確定要安裝「{SelectedTheme.Name}」到目前專案嗎？\n\n{SelectedTheme.Description}").ConfigureAwait(true);
        if (!ok) return;

        IsBusy = true;
        try
        {
            AppendLog($"安裝主題：{SelectedTheme.Name}…");
            var progress = new Progress<string>(AppendLog);
            var result = await _themes.InstallThemeAsync(_project.ProjectPath!, SelectedTheme, progress)
                .ConfigureAwait(true);
            AppendLog(result.Success
                ? $"主題 {SelectedTheme.Name} 安裝完成。"
                : "安裝失敗：\n" + result.CombinedOutput);
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveChirpyFormAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            var s = CollectChirpyFromForm();
            await _chirpy.SaveAsync(_project.ProjectPath!, s).ConfigureAwait(true);
            StatusMessage = "Chirpy 表單設定已寫入 _config.yml";
            AppendLog(StatusMessage);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = "儲存失敗：" + ex.Message;
            AppendLog(StatusMessage);
        }
    }

    [RelayCommand]
    private async Task SaveThemeConfigAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            await _config.SaveThemeConfigAsync(_project.ProjectPath!, ThemeConfigYaml).ConfigureAwait(true);
            AppendLog("原始 YAML 設定已儲存。");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog("儲存失敗：" + ex.Message);
        }
    }

    private void ApplyChirpyToForm(ChirpySettings s)
    {
        ChirpyTitle = s.Title;
        ChirpyTagline = s.Tagline;
        ChirpyDescription = s.Description;
        ChirpyUrl = s.Url;
        ChirpyBaseUrl = s.BaseUrl;
        ChirpyLang = s.Lang;
        ChirpyTimezone = s.Timezone;
        ChirpyThemeMode = s.ThemeMode;
        ChirpyAvatar = s.Avatar;
        ChirpyCdn = s.Cdn;
        ChirpySocialPreviewImage = s.SocialPreviewImage;
        ChirpyToc = s.Toc;
        ChirpyPaginate = s.Paginate;
        ChirpyGithubUsername = s.GithubUsername;
        ChirpyTwitterUsername = s.TwitterUsername;
        ChirpySocialName = s.SocialName;
        ChirpySocialEmail = s.SocialEmail;
        ChirpyFediverseHandle = s.FediverseHandle;
        ChirpySocialLinks = s.SocialLinks;
        ChirpyGoogleVerification = s.GoogleVerification;
        ChirpyBingVerification = s.BingVerification;
        ChirpyGoogleAnalyticsId = s.GoogleAnalyticsId;
        ChirpyGoatCounterId = s.GoatCounterId;
        ChirpyCommentsProvider = s.CommentsProvider;
        ChirpyDisqusShortname = s.DisqusShortname;
        ChirpyUtterancesRepo = s.UtterancesRepo;
        ChirpyUtterancesIssueTerm = s.UtterancesIssueTerm;
        ChirpyGiscusRepo = s.GiscusRepo;
        ChirpyGiscusRepoId = s.GiscusRepoId;
        ChirpyGiscusCategory = s.GiscusCategory;
        ChirpyGiscusCategoryId = s.GiscusCategoryId;
        ChirpyPwaEnabled = s.PwaEnabled;
        ChirpyPwaCacheEnabled = s.PwaCacheEnabled;
        ChirpyAssetsSelfHostEnabled = s.AssetsSelfHostEnabled;
        ChirpyEditPostEnabled = s.EditPostEnabled;
        ChirpyEditPostUrl = s.EditPostUrl;
    }

    private ChirpySettings CollectChirpyFromForm() => new()
    {
        Title = ChirpyTitle,
        Tagline = ChirpyTagline,
        Description = ChirpyDescription,
        Url = ChirpyUrl,
        BaseUrl = ChirpyBaseUrl,
        Lang = ChirpyLang,
        Timezone = ChirpyTimezone,
        ThemeMode = ChirpyThemeMode,
        Avatar = ChirpyAvatar,
        Cdn = ChirpyCdn,
        SocialPreviewImage = ChirpySocialPreviewImage,
        Toc = ChirpyToc,
        Paginate = ChirpyPaginate,
        GithubUsername = ChirpyGithubUsername,
        TwitterUsername = ChirpyTwitterUsername,
        SocialName = ChirpySocialName,
        SocialEmail = ChirpySocialEmail,
        FediverseHandle = ChirpyFediverseHandle,
        SocialLinks = ChirpySocialLinks,
        GoogleVerification = ChirpyGoogleVerification,
        BingVerification = ChirpyBingVerification,
        GoogleAnalyticsId = ChirpyGoogleAnalyticsId,
        GoatCounterId = ChirpyGoatCounterId,
        CommentsProvider = ChirpyCommentsProvider,
        DisqusShortname = ChirpyDisqusShortname,
        UtterancesRepo = ChirpyUtterancesRepo,
        UtterancesIssueTerm = ChirpyUtterancesIssueTerm,
        GiscusRepo = ChirpyGiscusRepo,
        GiscusRepoId = ChirpyGiscusRepoId,
        GiscusCategory = ChirpyGiscusCategory,
        GiscusCategoryId = ChirpyGiscusCategoryId,
        PwaEnabled = ChirpyPwaEnabled,
        PwaCacheEnabled = ChirpyPwaCacheEnabled,
        AssetsSelfHostEnabled = ChirpyAssetsSelfHostEnabled,
        EditPostEnabled = ChirpyEditPostEnabled,
        EditPostUrl = ChirpyEditPostUrl
    };

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
    }
}
