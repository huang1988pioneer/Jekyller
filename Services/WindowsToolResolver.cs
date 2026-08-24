using System.Diagnostics;

namespace Jekyller.Services;

internal static class WindowsToolResolver
{
    public static void Prepare(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = EnrichPath(psi.Environment["PATH"] ?? string.Empty);
        psi.Environment["PATH"] = path;
        ResolveCommand(psi, path);
    }

    private static string EnrichPath(string path)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var extras = new[]
        {
            Path.Combine(localAppData, "Programs", "Ruby", "bin"),
            @"C:\Ruby35-x64\bin",
            @"C:\Ruby34-x64\bin",
            @"C:\Ruby33-x64\bin",
            @"C:\Ruby32-x64\bin",
            Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.5.0", "bin"),
            Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.4.0", "bin"),
            Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.3.0", "bin"),
            Path.Combine(userProfile, ".local", "share", "gem", "ruby", "3.2.0", "bin"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps"),
            Path.Combine(programFiles, "Git", "cmd"),
            Path.Combine(programFiles, "GitHub CLI")
        };

        foreach (var extra in extras)
        {
            if (Directory.Exists(extra) && !path.Contains(extra, StringComparison.OrdinalIgnoreCase))
                path = extra + Path.PathSeparator + path;
        }

        return path;
    }

    private static void ResolveCommand(ProcessStartInfo psi, string path)
    {
        var resolved = ResolveExecutable(psi.FileName, path);
        if (resolved is null)
            return;

        var extension = Path.GetExtension(resolved);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = psi.Arguments;
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = string.IsNullOrWhiteSpace(arguments)
                ? $"/d /s /c \"\"{resolved}\"\""
                : $"/d /s /c \"\"{resolved}\" {arguments}\"";
            return;
        }

        psi.FileName = resolved;
    }

    private static string? ResolveExecutable(string fileName, string path)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var candidates = GetExecutableCandidates(fileName);
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

    private static IEnumerable<string> GetExecutableCandidates(string fileName)
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
