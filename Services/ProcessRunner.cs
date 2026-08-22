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

        // Ensure common tool paths are visible on Windows.
        if (OperatingSystem.IsWindows())
        {
            var path = psi.Environment["PATH"] ?? string.Empty;
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var extras = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ruby", "bin"),
                @"C:\Ruby35-x64\bin",
                @"C:\Ruby34-x64\bin",
                @"C:\Ruby33-x64\bin",
                @"C:\Ruby32-x64\bin",
                Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.5.0", "bin"),
                Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.4.0", "bin"),
                Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.3.0", "bin"),
                Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.2.0", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI")
            };

            foreach (var extra in extras)
            {
                if (Directory.Exists(extra) && !path.Contains(extra, StringComparison.OrdinalIgnoreCase))
                    path = extra + Path.PathSeparator + path;
            }

            psi.Environment["PATH"] = path;
            ResolveWindowsCommand(psi, path);
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

    private static void ResolveWindowsCommand(ProcessStartInfo psi, string path)
    {
        var resolved = ResolveWindowsExecutable(psi.FileName, path);
        if (resolved is null)
            return;

        var extension = Path.GetExtension(resolved);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = psi.Arguments;
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = string.IsNullOrWhiteSpace(arguments)
                ? $"/d /c \"\"{resolved}\"\""
                : $"/d /c \"\"{resolved}\" {arguments}\"";
            return;
        }

        psi.FileName = resolved;
    }

    private static string? ResolveWindowsExecutable(string fileName, string path)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var candidates = GetWindowsExecutableCandidates(fileName);
        if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
            return candidates.FirstOrDefault(File.Exists);

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetWindowsExecutableCandidates(string fileName)
    {
        if (Path.HasExtension(fileName))
        {
            yield return fileName;
            yield break;
        }

        yield return fileName + ".exe";
        yield return fileName + ".cmd";
        yield return fileName + ".bat";
        yield return fileName + ".com";
    }
}
