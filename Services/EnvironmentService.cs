using Jekyller.Models;

namespace Jekyller.Services;

public interface IEnvironmentService
{
    Task<IReadOnlyList<ToolStatus>> CheckToolsAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> InstallJekyllAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default);
    Task<ProcessResult> InstallBundlerAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default);
}

public sealed class EnvironmentService(IProcessRunner processRunner) : IEnvironmentService
{
    public async Task<IReadOnlyList<ToolStatus>> CheckToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = new (string Name, string File, string Args)[]
        {
            ("Ruby", "ruby", "-v"),
            ("Gem", "gem", "-v"),
            ("Bundler", "bundle", "-v"),
            ("Jekyll", "jekyll", "-v"),
            ("Git", "git", "--version"),
            ("GitHub CLI", "gh", "--version")
        };

        var results = new List<ToolStatus>();
        foreach (var (name, file, args) in tools)
        {
            var result = await processRunner.RunAsync(file, args, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = result.CombinedOutput.Replace("\r", " ").Replace("\n", " ").Trim();
            results.Add(new ToolStatus
            {
                Name = name,
                IsInstalled = result.Success && !string.IsNullOrWhiteSpace(text),
                Version = result.Success ? ExtractVersion(text) : string.Empty,
                Detail = text
            });
        }

        return results;
    }

    public Task<ProcessResult> InstallBundlerAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default)
        => processRunner.RunAsync("gem", "install bundler", output: output, cancellationToken: cancellationToken);

    public Task<ProcessResult> InstallJekyllAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default)
        => processRunner.RunAsync("gem", "install jekyll bundler", output: output, cancellationToken: cancellationToken);

    private static string ExtractVersion(string text)
    {
        // Prefer first x.y or x.y.z token.
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var cleaned = part.Trim().TrimStart('v', 'V');
            if (cleaned.Any(char.IsDigit) && cleaned.Contains('.'))
                return cleaned;
        }

        return text.Length > 40 ? text[..40] + "…" : text;
    }
}
