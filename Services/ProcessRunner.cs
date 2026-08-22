using System.Diagnostics;
using System.Text;

namespace Jekyller.Services;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool Success => ExitCode == 0;
    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StdErr))
                return StdOut.Trim();
            if (string.IsNullOrWhiteSpace(StdOut))
                return StdErr.Trim();
            return (StdOut.Trim() + Environment.NewLine + StdErr.Trim()).Trim();
        }
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environment = null,
        int? timeoutMs = null);

    Task<ProcessResult> RunShellAsync(
        string command,
        string? workingDirectory = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunShellAsync(
        string command,
        string? workingDirectory = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
            return RunAsync("cmd.exe", $"/c {command}", workingDirectory, output, cancellationToken);

        return RunAsync("/bin/bash", $"-lc \"{command.Replace("\"", "\\\"")}\"", workingDirectory, output, cancellationToken);
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environment = null,
        int? timeoutMs = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                    psi.Environment.Remove(pair.Key);
                else
                    psi.Environment[pair.Key] = pair.Value;
            }
        }

        WindowsToolResolver.Prepare(psi);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            output?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            output?.Report(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"無法啟動程序: {fileName}"
                };
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = $"無法啟動 {fileName}: {ex.Message}"
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeoutMs is int ms
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeoutCts is not null && timeoutMs is int msValue)
            timeoutCts.CancelAfter(msValue);

        var token = timeoutCts?.Token ?? cancellationToken;
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            if (cancellationToken.IsCancellationRequested)
                throw;

            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = $"程序逾時：{fileName} {arguments}"
            };
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString()
        };
    }

    private static string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            return workingDirectory;

        var current = Environment.CurrentDirectory;
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            return current;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
            return documents;

        return Path.GetTempPath();
    }

}
