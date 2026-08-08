using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Models;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class ContentViewModel : ViewModelBase
{
    private readonly IContentService _content;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;
    private readonly IMarkdownPreviewService _markdown;

    public ObservableCollection<ContentFile> Files { get; } = [];
    public ObservableCollection<PreviewBlock> PreviewBlocks { get; } = [];

    [ObservableProperty]
    public partial ContentFile? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowPreview { get; set; } = true;

    [ObservableProperty]
    public partial string NewPostTitle { get; set; } = "我的第一篇文章";

    [ObservableProperty]
    public partial string NewPostCategories { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPostTags { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPageTitle { get; set; } = "About";

    [ObservableProperty]
    public partial string NewPageFileName { get; set; } = "about.md";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "開啟專案後可編輯 Markdown 內容";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    private bool _loading;

    public ContentViewModel(
        IContentService content,
        IProjectContext project,
        IDialogService dialogs,
        IMarkdownPreviewService markdown)
    {
        _content = content;
        _project = project;
        _dialogs = dialogs;
        _markdown = markdown;
        _project.ProjectChanged += async (_, _) => await RefreshListAsync().ConfigureAwait(true);
        _ = RefreshListAsync();
    }

    partial void OnSelectedFileChanged(ContentFile? value)
    {
        _ = LoadSelectedAsync(value);
    }

    partial void OnEditorTextChanged(string value)
    {
        if (!_loading)
            IsDirty = true;
        RefreshPreview(value);
    }

    private void RefreshPreview(string? fullDocument)
    {
        PreviewMarkdown = _markdown.ExtractBody(fullDocument);
        PreviewBlocks.Clear();
        foreach (var block in _markdown.ToBlocks(PreviewMarkdown))
            PreviewBlocks.Add(block);
    }

    [RelayCommand]
    private async Task RefreshListAsync()
    {
        Files.Clear();
        EditorText = string.Empty;
        PreviewMarkdown = string.Empty;
        PreviewBlocks.Clear();
        SelectedFile = null;
        IsDirty = false;

        if (!_project.HasProject)
        {
            StatusMessage = "尚未開啟專案。";
            return;
        }

        var list = await _content.ListContentAsync(_project.ProjectPath!).ConfigureAwait(true);
        foreach (var f in list)
            Files.Add(f);

        StatusMessage = $"共 {Files.Count} 個內容檔案";
    }

    private async Task LoadSelectedAsync(ContentFile? file)
    {
        if (file is null)
        {
            _loading = true;
            EditorText = string.Empty;
            RefreshPreview(string.Empty);
            _loading = false;
            IsDirty = false;
            return;
        }

        try
        {
            _loading = true;
            EditorText = await _content.ReadAsync(file.FullPath).ConfigureAwait(true);
            RefreshPreview(EditorText);
            IsDirty = false;
            StatusMessage = "編輯中：" + file.RelativePath;
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
    private async Task SaveAsync()
    {
        if (SelectedFile is null)
        {
            await _dialogs.ShowMessageAsync("提示", "請先選擇檔案。").ConfigureAwait(true);
            return;
        }

        try
        {
            await _content.SaveAsync(SelectedFile.FullPath, EditorText).ConfigureAwait(true);
            IsDirty = false;
            StatusMessage = "已儲存 " + SelectedFile.RelativePath;
        }
        catch (Exception ex)
        {
            StatusMessage = "儲存失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            var file = await _content.CreatePostAsync(
                _project.ProjectPath!,
                NewPostTitle,
                NewPostCategories,
                NewPostTags).ConfigureAwait(true);

            await RefreshListAsync().ConfigureAwait(true);
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == file.FullPath);
            StatusMessage = "已建立文章：" + file.RelativePath;
        }
        catch (Exception ex)
        {
            StatusMessage = "建立失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreatePageAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟專案。").ConfigureAwait(true);
            return;
        }

        try
        {
            var file = await _content.CreatePageAsync(
                _project.ProjectPath!,
                NewPageTitle,
                NewPageFileName).ConfigureAwait(true);

            await RefreshListAsync().ConfigureAwait(true);
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == file.FullPath);
            StatusMessage = "已建立頁面：" + file.RelativePath;
        }
        catch (Exception ex)
        {
            StatusMessage = "建立失敗：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenHtmlPreview()
    {
        try
        {
            var html = _markdown.ToHtml(PreviewMarkdown);
            var path = Path.Combine(Path.GetTempPath(), "jekyller-md-preview.html");
            File.WriteAllText(path, html);
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
}
