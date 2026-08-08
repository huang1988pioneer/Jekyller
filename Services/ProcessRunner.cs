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
        CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Ensure common tool paths are visible on Windows.
        if (OperatingSystem.IsWindows())
        {
            var path = psi.Environment["PATH"] ?? string.Empty;
            var extras = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ruby", "bin"),
                @"C:\Ruby34-x64\bin",
                @"C:\Ruby33-x64\bin",
                @"C:\Ruby32-x64\bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI")
            };

            foreach (var extra in extras)
            {
                if (Directory.Exists(extra) && !path.Contains(extra, StringComparison.OrdinalIgnoreCase))
                    path = extra + Path.PathSeparator + path;
            }

            psi.Environment["PATH"] = path;
        }

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

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
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

            throw;
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString()
        };
    }
}
