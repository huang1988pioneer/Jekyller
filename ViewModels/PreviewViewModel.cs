using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jekyller.Services;

namespace Jekyller.ViewModels;

public partial class PreviewViewModel : ViewModelBase
{
    private readonly IJekyllServeService _serve;
    private readonly IProjectContext _project;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    public partial int Port { get; set; } = 4000;

    [ObservableProperty]
    public partial bool LiveReload { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool IsStarting { get; set; }

    public bool CanStart => !IsRunning && !IsStarting;

    [ObservableProperty]
    public partial string SiteUrl { get; set; } = "—";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "尚未啟動";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProjectPathDisplay { get; set; } = "尚未開啟專案";

    public PreviewViewModel(IJekyllServeService serve, IProjectContext project, IDialogService dialogs)
    {
        _serve = serve;
        _project = project;
        _dialogs = dialogs;

        _serve.StateChanged += (_, _) => PostToUi(SyncState);
        _serve.OutputReceived += (_, line) => PostToUi(() => AppendLog(line));

        _project.ProjectChanged += (_, _) =>
            ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
        ProjectPathDisplay = _project.HasProject ? _project.ProjectPath! : "尚未開啟專案";
        SyncState();
    }

    private void SyncState()
    {
        IsRunning = _serve.IsRunning;
        SiteUrl = _serve.SiteUrl ?? $"http://127.0.0.1:{Port}/";
        if (IsStarting && !IsRunning)
            StatusText = "啟動中…";
        else
            StatusText = IsRunning ? $"執行中 · {SiteUrl}" : "已停止";
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!_project.HasProject)
        {
            await _dialogs.ShowMessageAsync("提示", "請先開啟或建立 Jekyll 專案。").ConfigureAwait(true);
            return;
        }

        if (IsRunning || IsStarting)
        {
            await _dialogs.ShowMessageAsync("提示", "伺服器已在執行中。").ConfigureAwait(true);
            return;
        }

        IsStarting = true;
        StatusText = "啟動中…";
        try
        {
            AppendLog($"準備在 {_project.ProjectPath} 啟動 serve…");
            await _serve.StartAsync(_project.ProjectPath!, Port, LiveReload).ConfigureAwait(true);
            SyncState();
            if (IsRunning)
                AppendLog("程序已啟動，等待 Server address…");
        }
        catch (Exception ex)
        {
            AppendLog("啟動失敗：" + ex.Message);
            await _dialogs.ShowMessageAsync("啟動失敗", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsStarting = false;
            SyncState();
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _serve.StopAsync().ConfigureAwait(true);
        SyncState();
        AppendLog("已停止。");
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        _serve.OpenInBrowser();
        AppendLog("已在瀏覽器開啟 " + ( _serve.SiteUrl ?? SiteUrl));
    }

    [RelayCommand]
    private void ClearLog() => Log = string.Empty;

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
