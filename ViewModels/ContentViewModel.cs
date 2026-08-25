using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Helpers;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class ContentViewModel : ViewModelBase
{
    private readonly IContentService _content;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private readonly FrontMatterService _frontMatter;
    private readonly ISettingsService _settings;
    private readonly ISiteMigrationService _migration;
    private readonly IdleAutoSave _autoSave;
    private bool _loading;
    private bool _syncingFrontMatter;
    private List<ContentFile> _all = [];
    private CancellationTokenSource? _previewCts;

    public ContentViewModel(
        IContentService content,
        IProjectContext project,
        IDialogService dialogs,
        FrontMatterService frontMatter,
        ISettingsService settings,
        ISiteMigrationService migration)
    {
        _content = content;
        _project = project;
        _dialogs = dialogs;
        _frontMatter = frontMatter;
        _settings = settings;
        _migration = migration;

        _autoSave = new IdleAutoSave(
            () => IsDirty && SelectedFile is { IsDirectory: false },
            () => SaveCoreAsync(refreshList: false, auto: true));

        var saved = _settings.Current.MarkdownEditorMode;
        if (saved.Equals("Source", StringComparison.OrdinalIgnoreCase))
            EditorMode = MarkdownEditorMode.Source;
        else
            RefreshEditorModePresentation();

        AlignCorrespondingPreview();
        RefreshPreviewPresentation();

        _project.ProjectChanged += async (_, _) =>
        {
            OnPropertyChanged(nameof(ProjectPath));
            await RefreshListAsync().ConfigureAwait(true);
        };
        _ = RefreshListAsync();
    }

    public FrontMatterService FrontMatter => _frontMatter;
    public string? ProjectPath => _project.ProjectPath;

    public ObservableCollection<ContentFile> Files { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "文章日期（新到舊）",
        "文章日期（舊到新）",
        "名稱（A–Z）",
        "名稱（Z–A）"
    ];

    public IReadOnlyList<string> StatusOptions { get; } =
    [
        "全部內容",
        "已發布",
        "草稿",
        "頁面",
        "導覽",
        "未設定日期"
    ];

    [ObservableProperty]
    public partial ContentFile? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewBodyMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPreviewBody { get; set; }

    [ObservableProperty]
    public partial bool ShowPreview { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string NewPostTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPostCategories { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPostTags { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NewPostIsDraft { get; set; }

    [ObservableProperty]
    public partial string NewPageTitle { get; set; } = "About";

    [ObservableProperty]
    public partial string NewPageFileName { get; set; } = "about.md";

    [ObservableProperty]
    public partial string NewTabTitle { get; set; } = "About";

    [ObservableProperty]
    public partial string NewTabIcon { get; set; } = "fas fa-info-circle";

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortMode { get; set; } = "文章日期（新到舊）";

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = "全部內容";

    [ObservableProperty]
    public partial string FileCountLabel { get; set; } = "0 篇";

    [ObservableProperty]
    public partial string ContentSummary { get; set; } = "尚未載入內容";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool HasVisibleFiles { get; set; }

    [ObservableProperty]
    public partial bool ShowEmptyState { get; set; } = true;

    [ObservableProperty]
    public partial bool HasActiveFilters { get; set; }

    [ObservableProperty]
    public partial string EmptyStateTitle { get; set; } = "尚無內容";

    [ObservableProperty]
    public partial string EmptyStateDescription { get; set; } = "建立第一篇文章後，會顯示在這裡。";

    [ObservableProperty]
    public partial string FrontMatterTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterSlug { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterCategories { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterTags { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterImage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDraft { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "開啟專案後可編輯 Markdown 內容";

    [ObservableProperty]
    public partial string PreviewModeLabel { get; set; } = "即時預覽：開";

    [ObservableProperty]
    public partial string EditorStatistics { get; set; } = "正文 0 字元 · 1 行";

    [ObservableProperty]
    public partial MarkdownEditorMode EditorMode { get; set; } = MarkdownEditorMode.Wysiwyg;

    [ObservableProperty]
    public partial bool IsWysiwygMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSourceMode { get; set; }

    [ObservableProperty]
    public partial string EditorModeTitle { get; set; } = "WYSIWYG 視覺編輯";

    [ObservableProperty]
    public partial MarkdownPreviewKind PreviewKind { get; set; } = MarkdownPreviewKind.MarkdownOutput;

    [ObservableProperty]
    public partial bool IsRenderPreview { get; set; }

    [ObservableProperty]
    public partial bool IsMarkdownOutputPreview { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewCorrespondenceHint { get; set; } = "對應 WYSIWYG：產生的 Markdown 原文";

    [ObservableProperty]
    public partial string PreviewKindTitle { get; set; } = "Markdown 輸出";

    public string ReplaceBody(string markdown, string body) => _frontMatter.ReplaceBody(markdown, body);

    public string GetBody(string markdown) => _frontMatter.Parse(markdown).Body;

    partial void OnSelectedFileChanging(ContentFile? oldValue, ContentFile? newValue)
    {
        _autoSave.Cancel();
        if (_loading || !IsDirty || oldValue is null || oldValue.IsDirectory)
            return;

        var path = oldValue.FullPath;
        var text = EditorText;
        IsDirty = false;
        _ = PersistSilentlyAsync(path, text);
    }

    partial void OnSelectedFileChanged(ContentFile? value)
    {
        HasSelection = value is not null && !value.IsDirectory;
        if (value is not null && !value.IsDirectory)
            _ = LoadFileAsync(value);
        else
        {
            _loading = true;
            EditorText = string.Empty;
            PreviewMarkdown = string.Empty;
            UpdatePreviewBody(string.Empty);
            IsDirty = false;
            _loading = false;
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        UpdateEditorStatistics(value);
        UpdatePreviewBody(value);
        if (!_loading)
            MarkDirty();

        if (!_syncingFrontMatter)
            PopulateFrontMatter(value);

        SchedulePreviewUpdate(value);
    }

    private void UpdatePreviewBody(string markdown)
    {
        PreviewBodyMarkdown = MarkdownPreviewService.StripFrontMatter(markdown ?? string.Empty);
        HasPreviewBody = !string.IsNullOrWhiteSpace(PreviewBodyMarkdown);
    }

    private void UpdateEditorStatistics(string markdown)
    {
        var body = MarkdownPreviewService.StripFrontMatter(markdown);
        var characters = body.Count(character => !char.IsWhiteSpace(character));
        var lines = string.IsNullOrEmpty(body) ? 1 : body.Count(character => character == '\n') + 1;
        EditorStatistics = $"正文 {characters:N0} 字元 · {lines:N0} 行";
    }

    partial void OnFrontMatterTitleChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterDateChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterSlugChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterCategoriesChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterTagsChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterImageChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterDescriptionChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnIsDraftChanged(bool value) => UpdateEditorFromFrontMatter();

    partial void OnShowPreviewChanged(bool value)
    {
        PreviewModeLabel = value ? "即時預覽：開" : "即時預覽：關";
        if (value)
            SchedulePreviewUpdate(EditorText);
    }

    partial void OnEditorModeChanged(MarkdownEditorMode value)
    {
        RefreshEditorModePresentation();
        AlignCorrespondingPreview();
        _settings.SetMarkdownEditorMode(value == MarkdownEditorMode.Source ? "Source" : "Wysiwyg");
    }

    partial void OnPreviewKindChanged(MarkdownPreviewKind value) => RefreshPreviewPresentation();

    private void RefreshEditorModePresentation()
    {
        IsWysiwygMode = EditorMode == MarkdownEditorMode.Wysiwyg;
        IsSourceMode = EditorMode == MarkdownEditorMode.Source;
        EditorModeTitle = IsWysiwygMode ? "WYSIWYG 視覺編輯" : "Markdown 原始碼";
        RefreshPreviewPresentation();
    }

    private void AlignCorrespondingPreview()
    {
        var corresponding = EditorMode == MarkdownEditorMode.Source
            ? MarkdownPreviewKind.Render
            : MarkdownPreviewKind.MarkdownOutput;
        if (PreviewKind != corresponding)
            PreviewKind = corresponding;
        else
            RefreshPreviewPresentation();
    }

    private void RefreshPreviewPresentation()
    {
        IsRenderPreview = PreviewKind == MarkdownPreviewKind.Render;
        IsMarkdownOutputPreview = PreviewKind == MarkdownPreviewKind.MarkdownOutput;
        PreviewKindTitle = IsRenderPreview ? "渲染預覽" : "Markdown 輸出";
        PreviewCorrespondenceHint = (EditorMode, PreviewKind) switch
        {
            (MarkdownEditorMode.Wysiwyg, MarkdownPreviewKind.Render) =>
                "對應 WYSIWYG：Markdig 渲染結果",
            (MarkdownEditorMode.Wysiwyg, MarkdownPreviewKind.MarkdownOutput) =>
                "對應 WYSIWYG：產生的 Markdown 原文",
            (MarkdownEditorMode.Source, MarkdownPreviewKind.Render) =>
                "對應原始碼：即時渲染預覽",
            _ =>
                "對應原始碼：目前 Markdown 正文"
        };
    }

    [RelayCommand]
    private void SetWysiwygMode() => EditorMode = MarkdownEditorMode.Wysiwyg;

    [RelayCommand]
    private void SetSourceMode() => EditorMode = MarkdownEditorMode.Source;

    [RelayCommand]
    private void SetRenderPreview() => PreviewKind = MarkdownPreviewKind.Render;

    [RelayCommand]
    private void SetMarkdownOutputPreview() => PreviewKind = MarkdownPreviewKind.MarkdownOutput;

    [RelayCommand]
    private void ToggleEditorMode() =>
        EditorMode = EditorMode == MarkdownEditorMode.Wysiwyg
            ? MarkdownEditorMode.Source
            : MarkdownEditorMode.Wysiwyg;

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnSortModeChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void SchedulePreviewUpdate(string value)
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                if (token.IsCancellationRequested) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                        PreviewMarkdown = value;
                });
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    [RelayCommand]
    private void TogglePreview() => ShowPreview = !ShowPreview;

    [RelayCommand]
    private void ClearFilters()
    {
        Filter = string.Empty;
        StatusFilter = "全部內容";
    }

    [RelayCommand]
    private Task Refresh() => RefreshListAsync();

    private async Task RefreshListAsync()
    {
        var selectedPath = SelectedFile?.FullPath;
        if (!_project.HasProject)
        {
            _all = [];
            ApplyFilter();
            StatusMessage = "尚未開啟專案。";
            NewPostTitle = string.Empty;
            return;
        }

        try
        {
            _all = (await _content.ListContentAsync(_project.ProjectPath!).ConfigureAwait(true)).ToList();
            RefreshDefaultPostTitle();
            ApplyFilter();
            if (selectedPath is not null)
                SelectedFile = Files.FirstOrDefault(item =>
                    item.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            StatusMessage = $"共 {_all.Count} 個內容檔案";
        }
        catch (Exception ex)
        {
            StatusMessage = "載入失敗：" + ex.Message;
        }
    }

    public Task RefreshAsync() => RefreshListAsync();

    private void ApplyFilter()
    {
        Files.Clear();
        IEnumerable<ContentFile> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
        {
            q = q.Where(f =>
                f.RelativePath.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || f.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || f.DisplayTitle.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        }

        q = StatusFilter switch
        {
            "已發布" => q.Where(item => item.Kind == ContentKind.Post && item.IsPublished),
            "草稿" => q.Where(item => item.IsDraft || item.Kind == ContentKind.Draft),
            "頁面" => q.Where(item => item.Kind == ContentKind.Page),
            "導覽" => q.Where(item => item.Kind == ContentKind.Tab),
            "未設定日期" => q.Where(item => !item.HasArticleDate),
            _ => q
        };

        q = SortMode switch
        {
            "文章日期（舊到新）" => q.OrderByDescending(item => item.ArticleDate.HasValue)
                .ThenBy(item => item.ArticleDate)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            "名稱（A–Z）" => q.OrderBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            "名稱（Z–A）" => q.OrderByDescending(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderByDescending(item => item.ArticleDate.HasValue)
                .ThenByDescending(item => item.ArticleDate)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var item in q)
            Files.Add(item);

        var publishedCount = _all.Count(item => item.Kind == ContentKind.Post && item.IsPublished);
        var draftCount = _all.Count(item => item.IsDraft || item.Kind == ContentKind.Draft);
        var pageCount = _all.Count(item => item.Kind == ContentKind.Page);
        var tabCount = _all.Count(item => item.Kind == ContentKind.Tab);
        ContentSummary = $"{_all.Count} 個檔案 · {publishedCount} 文章 · {draftCount} 草稿 · {pageCount} 頁面 · {tabCount} 導覽";

        HasActiveFilters = !string.IsNullOrWhiteSpace(Filter) || StatusFilter != "全部內容";
        FileCountLabel = !HasActiveFilters
            ? $"{Files.Count} 篇"
            : $"顯示 {Files.Count} / {_all.Count} 篇";
        HasVisibleFiles = Files.Count > 0;
        ShowEmptyState = !HasVisibleFiles;
        EmptyStateTitle = _all.Count == 0 ? "尚無內容" : "找不到符合條件的內容";
        EmptyStateDescription = _all.Count == 0
            ? "按「新增文章」會在 _posts 建立 YYYY-MM-DD-slug.md；草稿會放到 _drafts。頁面與 Chirpy 導覽頁也可在這裡新增。"
            : "調整搜尋文字或狀態篩選後再試一次。";

        if (SelectedFile is not null && !Files.Contains(SelectedFile))
            SelectedFile = null;
    }

    private async Task LoadFileAsync(ContentFile item)
    {
        _autoSave.Cancel();
        _loading = true;
        try
        {
            EditorText = await _content.ReadAsync(item.FullPath);
            PreviewMarkdown = EditorText;
            UpdatePreviewBody(EditorText);
            PopulateFrontMatter(EditorText);

            var parsed = _frontMatter.Parse(EditorText);
            if ((item.Kind == ContentKind.Post || item.Kind == ContentKind.Draft)
                && string.IsNullOrWhiteSpace(GetField(parsed, "title")))
            {
                parsed.Fields["title"] = JekyllLocalPreview.FallbackPostTitle(item.Name);
                EditorText = _frontMatter.Write(parsed);
                FrontMatterTitle = parsed.Fields["title"];
                PreviewMarkdown = EditorText;
                await _content.SaveAsync(item.FullPath, EditorText);
                StatusMessage = $"已補上標題「{FrontMatterTitle}」，本機預覽才能列出這篇文章。";
            }
            else
            {
                StatusMessage = item.RelativePath;
            }

            IsDirty = false;
        }
        catch (Exception ex)
        {
            StatusMessage = "讀取失敗：" + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private Task SaveAsync() => SaveCoreAsync(refreshList: true, auto: false);

    private void MarkDirty()
    {
        IsDirty = true;
        _autoSave.Schedule();
    }

    private async Task SaveCoreAsync(bool refreshList, bool auto)
    {
        if (SelectedFile is null || SelectedFile.IsDirectory)
        {
            if (!auto)
                StatusMessage = "請先選擇檔案";
            return;
        }

        try
        {
            await _content.SaveAsync(SelectedFile.FullPath, EditorText);
            IsDirty = false;
            _autoSave.Cancel();
            StatusMessage = auto
                ? $"已自動儲存：{SelectedFile.RelativePath}"
                : $"已儲存：{SelectedFile.RelativePath}";
            if (refreshList)
                await RefreshListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = auto ? $"自動儲存失敗：{ex.Message}" : "儲存失敗：" + ex.Message;
        }
    }

    private async Task PersistSilentlyAsync(string fullPath, string text)
    {
        try
        {
            await _content.SaveAsync(fullPath, text);
            StatusMessage = $"已自動儲存：{Path.GetFileName(fullPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"自動儲存失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        if (!await RequireProjectAsync()) return;

        if (string.IsNullOrWhiteSpace(NewPostTitle))
            NewPostTitle = _content.NextDefaultPostTitle(_project.ProjectPath!);

        try
        {
            var file = await _content.CreatePostAsync(
                _project.ProjectPath!,
                NewPostTitle.Trim(),
                NewPostCategories,
                NewPostTags,
                NewPostIsDraft).ConfigureAwait(true);

            StatusMessage = "已建立文章：" + file.RelativePath;
            NewPostTitle = string.Empty;
            RefreshDefaultPostTitle();
            await RefreshListAsync().ConfigureAwait(true);
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == file.FullPath) ?? file;
        }
        catch (Exception ex)
        {
            StatusMessage = "建立失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreatePageAsync()
    {
        if (!await RequireProjectAsync()) return;

        try
        {
            var file = await _content.CreatePageAsync(
                _project.ProjectPath!,
                NewPageTitle,
                NewPageFileName).ConfigureAwait(true);

            StatusMessage = "已建立頁面：" + file.RelativePath;
            await RefreshListAsync().ConfigureAwait(true);
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == file.FullPath) ?? file;
        }
        catch (Exception ex)
        {
            StatusMessage = "建立失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreateTabAsync()
    {
        if (!await RequireProjectAsync()) return;

        try
        {
            var file = await _content.CreateTabAsync(
                _project.ProjectPath!,
                NewTabTitle,
                NewTabIcon).ConfigureAwait(true);

            StatusMessage = "已建立導覽頁：" + file.RelativePath;
            await RefreshListAsync().ConfigureAwait(true);
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == file.FullPath) ?? file;
        }
        catch (Exception ex)
        {
            StatusMessage = "建立失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedFile is null) return;

        var ok = await _dialogs.ConfirmAsync(
            "刪除內容",
            $"確定刪除 {SelectedFile.RelativePath}？此動作無法復原。").ConfigureAwait(true);
        if (!ok) return;

        try
        {
            await _content.DeleteAsync(SelectedFile.FullPath).ConfigureAwait(true);
            StatusMessage = $"已刪除：{SelectedFile.RelativePath}";
            SelectedFile = null;
            EditorText = string.Empty;
            PreviewMarkdown = string.Empty;
            UpdatePreviewBody(string.Empty);
            await RefreshListAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = "刪除失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task UploadCoverImageAsync()
    {
        if (!await RequireProjectAsync()) return;
        if (!HasSelection)
        {
            StatusMessage = "請先選擇文章";
            return;
        }

        var path = await DialogHelper.PickFileAsync("選擇封面圖片", [DialogHelper.Images]);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var asset = MediaAssetService.Import(_project.ProjectPath!, path, MediaKind.Image);
            FrontMatterImage = asset.PublicUrl;
            StatusMessage = $"封面已上傳至 assets/{asset.Folder}/{Path.GetFileName(asset.DestinationPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task ExportSelectedHugoAsync() => ExportSelectedAsync(StaticSiteKind.Hugo);

    [RelayCommand]
    private Task ExportSelectedHexoAsync() => ExportSelectedAsync(StaticSiteKind.Hexo);

    [RelayCommand]
    private Task ExportAllHugoAsync() => ExportAllAsync(StaticSiteKind.Hugo);

    [RelayCommand]
    private Task ExportAllHexoAsync() => ExportAllAsync(StaticSiteKind.Hexo);

    private async Task ExportSelectedAsync(StaticSiteKind target)
    {
        if (!await RequireProjectAsync()) return;
        if (SelectedFile is null || SelectedFile.IsDirectory)
        {
            StatusMessage = "請先選擇文章";
            return;
        }

        var name = StaticSiteDetector.DisplayName(target);
        try
        {
            var converted = ArticleFormatConverter.Convert(
                EditorText,
                SelectedFile.FullPath,
                SelectedFile.RelativePath,
                SelectedFile.Kind,
                StaticSiteKind.Jekyll,
                target);
            var path = await DialogHelper.PickSaveFileAsync(
                $"匯出為 {name} Markdown",
                converted.FileName);
            if (string.IsNullOrWhiteSpace(path))
                return;

            await File.WriteAllTextAsync(path, converted.Markdown, new System.Text.UTF8Encoding(false));
            StatusMessage = $"已匯出 {name} 文章：{path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯出失敗：{ex.Message}";
        }
    }

    private async Task ExportAllAsync(StaticSiteKind target)
    {
        if (!await RequireProjectAsync()) return;

        var name = StaticSiteDetector.DisplayName(target);
        var folder = await _dialogs.PickFolderAsync($"選擇 {name} 匯出資料夾").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var source = _project.ProjectPath!;
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
        {
            await _dialogs.ShowMessageAsync("無法匯出", "請選擇不同於目前 Jekyll 專案的資料夾。")
                .ConfigureAwait(true);
            return;
        }

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            var ok = await _dialogs.ConfirmAsync(
                "資料夾不是空的",
                $"會把 Jekyll 站台遷移成 {name} 並寫入 {folder}。仍要繼續嗎？").ConfigureAwait(true);
            if (!ok)
                return;
        }

        try
        {
            StatusMessage = $"正在匯出為 {name}…";
            var result = await _migration.MigrateAsync(new SiteMigrationRequest
            {
                SourcePath = source,
                DestinationPath = folder,
                SourceKind = StaticSiteKind.Jekyll,
                TargetKind = target
            }).ConfigureAwait(true);
            StatusMessage = result.Summary;
            await _dialogs.ShowMessageAsync(
                    result.Success ? $"已匯出為 {name}" : "匯出失敗",
                    result.Summary)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯出失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenHtmlPreviewAsync()
    {
        try
        {
            var html = MarkdownPreviewService.ToHtmlDocument(
                EditorText,
                SelectedFile?.Name ?? "preview");
            html = MediaAssetService.ToPreviewHtml(html, _project.ProjectPath);
            var dir = Path.Combine(Path.GetTempPath(), "JekyllerPreview");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "preview.html");
            await File.WriteAllTextAsync(path, html);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = "已在瀏覽器開啟 HTML 預覽";
        }
        catch (Exception ex)
        {
            StatusMessage = "開啟預覽失敗：" + ex.Message;
        }
    }

    private void RefreshDefaultPostTitle()
    {
        if (!_project.HasProject)
        {
            NewPostTitle = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(NewPostTitle) && !PostNaming.IsDefaultTitle(NewPostTitle))
            return;

        NewPostTitle = _content.NextDefaultPostTitle(_project.ProjectPath!);
    }

    private async Task<bool> RequireProjectAsync()
    {
        if (_project.HasProject) return true;
        await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
        StatusMessage = "尚未開啟專案。";
        return false;
    }

    private void PopulateFrontMatter(string text)
    {
        var document = _frontMatter.Parse(text);
        _syncingFrontMatter = true;
        try
        {
            var title = GetField(document, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = JekyllLocalPreview.FallbackPostTitle(SelectedFile?.Name);
            FrontMatterTitle = title;
            FrontMatterDate = GetField(document, "date");
            FrontMatterSlug = GetField(document, "slug");
            FrontMatterCategories = GetField(document, "categories");
            FrontMatterTags = GetField(document, "tags");
            FrontMatterImage = FirstField(document, "image.path", "image");
            FrontMatterDescription = GetField(document, "description");
            IsDraft = SelectedFile?.Kind == ContentKind.Draft
                      || GetField(document, "published").Equals("false", StringComparison.OrdinalIgnoreCase)
                      || GetField(document, "draft").Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _syncingFrontMatter = false;
        }
    }

    private void UpdateEditorFromFrontMatter()
    {
        if (_loading || _syncingFrontMatter) return;

        var document = _frontMatter.Parse(EditorText);
        var title = string.IsNullOrWhiteSpace(FrontMatterTitle)
            ? JekyllLocalPreview.FallbackPostTitle(SelectedFile?.Name)
            : FrontMatterTitle;
        SetField(document, "title", title);
        SetField(document, "date", FrontMatterDate);
        SetField(document, "slug", FrontMatterSlug);
        SetField(document, "categories", FrontMatterCategories);
        SetField(document, "tags", FrontMatterTags);
        SetField(document, "image", FrontMatterImage);
        if (!string.IsNullOrWhiteSpace(FrontMatterImage))
            document.Fields["image.path"] = FrontMatterImage.Trim();
        else
            document.Fields.Remove("image.path");
        SetField(document, "description", FrontMatterDescription);

        if (IsDraft)
            document.Fields["published"] = "false";
        else
            document.Fields.Remove("published");
        document.Fields.Remove("draft");

        _syncingFrontMatter = true;
        try
        {
            EditorText = _frontMatter.Write(document);
        }
        finally
        {
            _syncingFrontMatter = false;
        }
    }

    private static string GetField(FrontMatterDocument document, string key) =>
        document.Fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static string FirstField(FrontMatterDocument document, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetField(document, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static void SetField(FrontMatterDocument document, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            document.Fields.Remove(key);
        else
            document.Fields[key] = value.Trim();
    }
}
