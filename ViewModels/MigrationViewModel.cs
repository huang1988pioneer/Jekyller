using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class MigrationViewModel : ViewModelBase
{
    public const string HugoToJekyll = "Hugo → Jekyll";
    public const string HexoToJekyll = "Hexo → Jekyll";
    public const string JekyllToHugo = "Jekyll → Hugo";
    public const string JekyllToHexo = "Jekyll → Hexo";

    private readonly ISiteMigrationService _migration;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private bool _suggestingDestination;

    public IReadOnlyList<string> Directions { get; } =
    [
        HugoToJekyll,
        HexoToJekyll,
        JekyllToHugo,
        JekyllToHexo
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceKindLabel { get; set; } = "尚未選擇來源";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    [NotifyPropertyChangedFor(nameof(DestPathDisplay))]
    [NotifyPropertyChangedFor(nameof(CanOpenAfterMigrate))]
    public partial string SelectedDirection { get; set; } = HugoToJekyll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    [NotifyPropertyChangedFor(nameof(DestPathDisplay))]
    public partial string DestParent { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    [NotifyPropertyChangedFor(nameof(DestPathDisplay))]
    public partial string DestName { get; set; } = "migrated-site";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool OpenAfterMigrate { get; set; } = true;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "把 Hugo / Hexo 轉成 Jekyll，或把目前 Jekyll 站台匯出成 Hugo / Hexo。";

    public string DestPathDisplay =>
        string.IsNullOrWhiteSpace(DestParent) || string.IsNullOrWhiteSpace(DestName)
            ? "尚未指定目標"
            : Path.Combine(DestParent.Trim(), DestName.Trim());

    public bool CanOpenAfterMigrate => ParseDirection(SelectedDirection).To == StaticSiteKind.Jekyll;

    public bool CanMigrate =>
        !IsBusy
        && Directory.Exists(SourcePath)
        && !string.IsNullOrWhiteSpace(DestParent)
        && !string.IsNullOrWhiteSpace(DestName);

    public MigrationViewModel(
        ISiteMigrationService migration,
        IProjectContext project,
        IDialogService dialogs)
    {
        _migration = migration;
        _project = project;
        _dialogs = dialogs;
        _project.ProjectChanged += (_, _) => UseCurrentProjectAsSource();
        UseCurrentProjectAsSource();
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var folder = await _dialogs.PickFolderAsync("選擇來源站台資料夾").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folder))
            return;
        SourcePath = folder;
    }

    [RelayCommand]
    private async Task BrowseDestParentAsync()
    {
        var folder = await _dialogs.PickFolderAsync("選擇目標父資料夾").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folder))
            return;
        DestParent = folder;
    }

    [RelayCommand]
    private void UseCurrentProject() => UseCurrentProjectAsSource();

    [RelayCommand]
    private void DetectSource() => RefreshSourceKind(selectDirection: true);

    [RelayCommand]
    private async Task MigrateAsync()
    {
        if (!CanMigrate)
            return;

        var (from, to) = ParseDirection(SelectedDirection);
        var detected = _migration.Detect(SourcePath);
        if (detected != StaticSiteKind.Unknown && detected != from)
        {
            var ok = await _dialogs.ConfirmAsync(
                "來源類型可能不符",
                $"資料夾偵測為 {StaticSiteDetector.DisplayName(detected)}，但選擇的方向是 {SelectedDirection}。仍要繼續嗎？")
                .ConfigureAwait(true);
            if (!ok)
                return;
            from = detected;
            if (!_migration.IsSupported(from, to))
            {
                await _dialogs.ShowMessageAsync(
                    "無法遷移",
                    $"不支援 {StaticSiteDetector.DisplayName(from)} → {StaticSiteDetector.DisplayName(to)}。")
                    .ConfigureAwait(true);
                return;
            }
        }

        var destination = DestPathDisplay;
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            var ok = await _dialogs.ConfirmAsync(
                "目標資料夾不是空的",
                $"{destination} 已有檔案，遷移會寫入或覆寫對應檔案。仍要繼續嗎？")
                .ConfigureAwait(true);
            if (!ok)
                return;
        }

        IsBusy = true;
        StatusMessage = "正在遷移…";
        AppendLog($"開始 {SelectedDirection}");
        try
        {
            var progress = new Progress<string>(AppendLog);
            var result = await _migration.MigrateAsync(
                    new SiteMigrationRequest
                    {
                        SourcePath = SourcePath,
                        DestinationPath = destination,
                        SourceKind = from,
                        TargetKind = to
                    },
                    progress)
                .ConfigureAwait(true);

            StatusMessage = result.Summary;
            if (!result.Success)
            {
                await _dialogs.ShowMessageAsync("遷移失敗", result.Message).ConfigureAwait(true);
                return;
            }

            await _dialogs.ShowMessageAsync("遷移完成", result.Summary).ConfigureAwait(true);
            if (OpenAfterMigrate && to == StaticSiteKind.Jekyll && Directory.Exists(result.DestinationPath))
                _project.SetProject(result.DestinationPath);
        }
        catch (Exception ex)
        {
            StatusMessage = "遷移失敗：" + ex.Message;
            AppendLog(StatusMessage);
            await _dialogs.ShowMessageAsync("遷移失敗", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSourcePathChanged(string value)
    {
        RefreshSourceKind(selectDirection: true);
        SuggestDestination();
    }

    partial void OnSelectedDirectionChanged(string value)
    {
        if (!_suggestingDestination)
            SuggestDestination();
    }

    private void UseCurrentProjectAsSource()
    {
        if (_project.HasProject)
            SourcePath = _project.ProjectPath!;
        else
            RefreshSourceKind(selectDirection: false);
    }

    private void RefreshSourceKind(bool selectDirection)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !Directory.Exists(SourcePath))
        {
            SourceKindLabel = "尚未選擇來源";
            return;
        }

        var kind = _migration.Detect(SourcePath);
        SourceKindLabel = kind == StaticSiteKind.Unknown
            ? "無法辨識（請確認資料夾含 Hugo、Hexo 或 Jekyll 設定）"
            : $"偵測結果：{StaticSiteDetector.DisplayName(kind)} 站台";

        if (!selectDirection)
            return;

        _suggestingDestination = true;
        try
        {
            SelectedDirection = kind switch
            {
                StaticSiteKind.Hugo => HugoToJekyll,
                StaticSiteKind.Hexo => HexoToJekyll,
                StaticSiteKind.Jekyll when SelectedDirection is JekyllToHugo or JekyllToHexo
                    => SelectedDirection,
                StaticSiteKind.Jekyll => JekyllToHugo,
                _ => SelectedDirection
            };
        }
        finally
        {
            _suggestingDestination = false;
        }
    }

    private void SuggestDestination()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !Directory.Exists(SourcePath))
            return;

        var parent = Directory.GetParent(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is not null)
            DestParent = parent.FullName;

        var name = Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var suffix = ParseDirection(SelectedDirection).To switch
        {
            StaticSiteKind.Jekyll => "jekyll",
            StaticSiteKind.Hugo => "hugo",
            StaticSiteKind.Hexo => "hexo",
            _ => "migrated"
        };
        DestName = $"{name}-{suffix}";
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        Log = string.IsNullOrEmpty(Log)
            ? line
            : Log + Environment.NewLine + line;
    }

    public static (StaticSiteKind From, StaticSiteKind To) ParseDirection(string? direction) => direction switch
    {
        HugoToJekyll => (StaticSiteKind.Hugo, StaticSiteKind.Jekyll),
        HexoToJekyll => (StaticSiteKind.Hexo, StaticSiteKind.Jekyll),
        JekyllToHugo => (StaticSiteKind.Jekyll, StaticSiteKind.Hugo),
        JekyllToHexo => (StaticSiteKind.Jekyll, StaticSiteKind.Hexo),
        _ => (StaticSiteKind.Unknown, StaticSiteKind.Unknown)
    };
}
