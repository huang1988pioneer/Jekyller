using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace Jekyller.Services;

public interface IJekyllServeService : IDisposable
{
    bool IsRunning { get; }
    string? SiteUrl { get; }
    int Port { get; }
    event EventHandler? StateChanged;
    event EventHandler<string>? OutputReceived;

    Task StartAsync(string projectPath, int port = 4000, bool liveReload = true, CancellationToken cancellationToken = default);
    Task StopAsync();
    void OpenInBrowser();
}

public sealed class JekyllServeService : IJekyllServeService
{
    private Process? _process;
    private StreamWriter? _stdin;
    private string _baseUrl = string.Empty;
    private readonly object _gate = new();

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _process is { HasExited: false };
        }
    }

    public string? SiteUrl { get; private set; }
    public int Port { get; private set; } = 4000;

    public event EventHandler? StateChanged;
    public event EventHandler<string>? OutputReceived;

    public async Task StartAsync(
        string projectPath,
        int port = 4000,
        bool liveReload = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            throw new InvalidOperationException("專案路徑無效。");

        await StopCoreAsync(notifyWhenIdle: false).ConfigureAwait(false);
        var clean = JekyllWorkspace.CleanGeneratedCaches(projectPath, new Progress<string>(message =>
            OutputReceived?.Invoke(this, message)));
        if (clean is not null)
            throw new InvalidOperationException(clean.CombinedOutput);

        Port = port <= 0 ? 4000 : port;
        _baseUrl = ReadBaseUrl(projectPath);
        SiteUrl = JekyllLocalPreview.BuildSiteUrl(Port, _baseUrl);
        TryFreePort(Port);

        var serveArgs = JekyllLocalPreview.BuildServeArguments(
            Port,
            liveReload,
            OperatingSystem.IsWindows());
        OutputReceived?.Invoke(this, "本機預覽會包含草稿與未來日期文章。");
        if (!string.IsNullOrEmpty(_baseUrl))
            OutputReceived?.Invoke(this, $"將使用 baseurl {_baseUrl} → {SiteUrl}");
        var attempts = new (string FileName, string Arguments)[]
        {
            ("ruby", $"-S bundle exec jekyll {serveArgs}"),
            ("bundle", $"exec jekyll {serveArgs}"),
            ("ruby", $"-S jekyll {serveArgs}"),
            ("jekyll", serveArgs)
        };

        Exception? lastError = null;
        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutputReceived?.Invoke(this, $"啟動：{attempt.FileName} {attempt.Arguments}");

            Process process;
            try
            {
                process = CreateServeProcess(projectPath, attempt.FileName, attempt.Arguments);
                if (!process.Start())
                {
                    process.Dispose();
                    lastError = new InvalidOperationException($"無法啟動 {attempt.FileName}。");
                    continue;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                OutputReceived?.Invoke(this, $"{attempt.FileName} 啟動失敗：{ex.Message}");
                continue;
            }

            try
            {
                if (process.StartInfo.RedirectStandardInput)
                    _stdin = process.StandardInput;
            }
            catch
            {
                _stdin = null;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_gate)
                _process = process;

            process.EnableRaisingEvents = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            var survived = await WaitForSurvivalAsync(process, cancellationToken).ConfigureAwait(false);
            if (survived)
                return;

            var code = SafeExitCode(process);
            OutputReceived?.Invoke(this, $"程序立即結束（代碼 {code}），改試下一種方式…");
            await ForgetAsync(process).ConfigureAwait(false);
            lastError = new InvalidOperationException($"{attempt.FileName} 立即結束，代碼 {code}。");
        }

        SiteUrl = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        throw lastError ?? new InvalidOperationException("無法啟動 jekyll serve。請確認已安裝 Ruby / Bundler / Jekyll。");
    }

    private Process CreateServeProcess(string projectPath, string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = projectPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        WindowsToolResolver.Prepare(psi);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = false };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            HandleLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            HandleLine(e.Data);
        };
        process.Exited += (_, _) => OnProcessExited(process);
        return process;
    }

    private static async Task<bool> WaitForSurvivalAsync(Process process, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 8; i++)
        {
            if (process.HasExited)
                return false;
            try
            {
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return !process.HasExited;
            }
        }

        return !process.HasExited;
    }

    public Task StopAsync() => StopCoreAsync(notifyWhenIdle: true);

    private async Task StopCoreAsync(bool notifyWhenIdle)
    {
        Process? process;
        StreamWriter? stdin;
        lock (_gate)
        {
            process = _process;
            stdin = _stdin;
            _process = null;
            _stdin = null;
        }

        if (process is null)
        {
            if (notifyWhenIdle)
            {
                SiteUrl = null;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        await KillAsync(process, stdin, notify: true).ConfigureAwait(false);
    }

    private async Task ForgetAsync(Process process)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_process, process))
                _process = null;
            _stdin = null;
        }

        await KillAsync(process, stdin: null, notify: false).ConfigureAwait(false);
    }

    private async Task KillAsync(Process process, StreamWriter? stdin, bool notify)
    {
        try
        {
            stdin?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (!process.HasExited)
            {
                if (notify)
                    OutputReceived?.Invoke(this, "停止 jekyll serve…");
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (notify)
                OutputReceived?.Invoke(this, "停止時發生錯誤：" + ex.Message);
        }
        finally
        {
            process.Dispose();
            SiteUrl = null;
            if (notify)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ReadBaseUrl(string projectPath)
    {
        foreach (var name in new[] { "_config.yml", "_config.yaml" })
        {
            var path = Path.Combine(projectPath, name);
            if (!File.Exists(path))
                continue;
            try
            {
                return JekyllLocalPreview.ParseBaseUrlFromYaml(File.ReadAllText(path));
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    public void OpenInBrowser()
    {
        var url = SiteUrl ?? JekyllLocalPreview.BuildSiteUrl(Port, _baseUrl);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private void OnProcessExited(Process process)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
                return;
            _process = null;
            _stdin = null;
        }

        var code = SafeExitCode(process);
        SiteUrl = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        OutputReceived?.Invoke(this, $"[serve 已結束，結束代碼 {code}]");
    }

    private void HandleLine(string line)
    {
        OutputReceived?.Invoke(this, line);

        var m = Regex.Match(line, @"https?://[^\s]+", RegexOptions.IgnoreCase);
        if (m.Success && (line.Contains("Server address", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("Server running", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("http://localhost", StringComparison.OrdinalIgnoreCase)))
        {
            SiteUrl = JekyllLocalPreview.EnsureSiteUrl(m.Value, Port, _baseUrl);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private void TryFreePort(int port)
    {
        if (!OperatingSystem.IsWindows() || port <= 0)
            return;

        try
        {
            var inUse = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endPoint => endPoint.Port == port);
            if (!inUse)
                return;
        }
        catch
        {
            return;
        }

        OutputReceived?.Invoke(this, $"連接埠 {port} 已被占用，嘗試釋放先前的 serve 程序…");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments =
                    $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | ForEach-Object {{ Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var killer = Process.Start(psi);
            killer?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, "釋放連接埠失敗：" + ex.Message);
        }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch { /* ignore */ }
    }
}
