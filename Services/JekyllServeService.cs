using System.Diagnostics;
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

        await StopAsync().ConfigureAwait(false);
        var clean = JekyllWorkspace.CleanGeneratedCaches(projectPath, new Progress<string>(message =>
            OutputReceived?.Invoke(this, message)));
        if (clean is not null)
            throw new InvalidOperationException(clean.CombinedOutput);

        Port = port <= 0 ? 4000 : port;
        SiteUrl = $"http://127.0.0.1:{Port}/";

        var args = $"exec jekyll serve --host 127.0.0.1 --port {Port}";
        if (liveReload)
            args += " --livereload";

        var psi = new ProcessStartInfo
        {
            FileName = "bundle",
            Arguments = args,
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        WindowsToolResolver.Prepare(psi);

        Process process;
        try
        {
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
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
            process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_process, process))
                        _process = null;
                }

                SiteUrl = null;
                StateChanged?.Invoke(this, EventArgs.Empty);
                OutputReceived?.Invoke(this, "[serve 已結束]");
            };

            if (!process.Start())
                throw new InvalidOperationException("無法啟動 bundle exec jekyll serve。");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_gate)
                _process = process;

            StateChanged?.Invoke(this, EventArgs.Empty);
            OutputReceived?.Invoke(this, $"啟動 jekyll serve（port {Port}）…");
        }
        catch (Exception ex)
        {
            // Fallback: try jekyll directly without bundle
            OutputReceived?.Invoke(this, $"bundle 啟動失敗（{ex.Message}），改試 jekyll…");
            await StartWithJekyllDirectAsync(projectPath, port, liveReload, cancellationToken).ConfigureAwait(false);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task StartWithJekyllDirectAsync(string projectPath, int port, bool liveReload, CancellationToken cancellationToken)
    {
        var args = $"serve --host 127.0.0.1 --port {port}";
        if (liveReload)
            args += " --livereload";

        var psi = new ProcessStartInfo
        {
            FileName = "jekyll",
            Arguments = args,
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        WindowsToolResolver.Prepare(psi);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data); };
        process.Exited += (_, _) =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                    _process = null;
            }

            SiteUrl = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            OutputReceived?.Invoke(this, "[serve 已結束]");
        };

        if (!process.Start())
            throw new InvalidOperationException("無法啟動 jekyll serve。請確認已安裝 Jekyll / Bundler。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
            _process = process;

        Port = port;
        SiteUrl = $"http://127.0.0.1:{port}/";
        StateChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            SiteUrl = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                OutputReceived?.Invoke(this, "停止 jekyll serve…");
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, "停止時發生錯誤：" + ex.Message);
        }
        finally
        {
            process.Dispose();
            SiteUrl = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenInBrowser()
    {
        var url = SiteUrl ?? $"http://127.0.0.1:{Port}/";
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

    private void HandleLine(string line)
    {
        OutputReceived?.Invoke(this, line);

        // Server address: http://127.0.0.1:4000/
        var m = Regex.Match(line, @"https?://[^\s]+", RegexOptions.IgnoreCase);
        if (m.Success && (line.Contains("Server address", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("Server running", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("http://localhost", StringComparison.OrdinalIgnoreCase)))
        {
            SiteUrl = m.Value.TrimEnd('/');
            if (!SiteUrl.EndsWith('/'))
                SiteUrl += "/";
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch { /* ignore */ }
    }
}
