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
    private const string RubyWingetPackageId = "RubyInstallerTeam.RubyWithDevKit.3.4";

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

    public async Task<ProcessResult> InstallBundlerAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default)
    {
        var gem = await EnsureGemAvailableAsync(output, cancellationToken).ConfigureAwait(false);
        if (!gem.Success)
            return gem;

        return await processRunner.RunAsync("gem", "install bundler", output: output, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProcessResult> InstallJekyllAsync(IProgress<string>? output = null, CancellationToken cancellationToken = default)
    {
        var gem = await EnsureGemAvailableAsync(output, cancellationToken).ConfigureAwait(false);
        if (!gem.Success)
            return gem;

        return await processRunner.RunAsync("gem", "install jekyll bundler", output: output, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProcessResult> EnsureGemAvailableAsync(
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        var gem = await processRunner.RunAsync("gem", "-v", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (gem.Success)
            return gem;

        if (!OperatingSystem.IsWindows())
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = "找不到 Ruby/Gem。請先安裝 Ruby，再重新執行安裝 Jekyll + Bundler。"
            };
        }

        output?.Report("找不到 Ruby/Gem，正在嘗試透過 winget 安裝 RubyInstaller with DevKit...");
        var wingetArgs =
            $"install --id {RubyWingetPackageId} -e --silent --accept-package-agreements --accept-source-agreements";
        var rubyInstall = await processRunner.RunAsync(
                "winget",
                wingetArgs,
                output: output,
                cancellationToken: cancellationToken,
                timeoutMs: 20 * 60 * 1000)
            .ConfigureAwait(false);

        if (!rubyInstall.Success)
        {
            return new ProcessResult
            {
                ExitCode = rubyInstall.ExitCode,
                StdOut = rubyInstall.StdOut,
                StdErr = (rubyInstall.StdErr + Environment.NewLine
                    + "無法自動安裝 Ruby。請先安裝 RubyInstaller with DevKit，或確認 winget 可用後再試一次。").Trim()
            };
        }

        output?.Report("Ruby 安裝完成，重新檢查 gem...");
        gem = await processRunner.RunAsync("gem", "-v", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (gem.Success)
            return gem;

        return new ProcessResult
        {
            ExitCode = gem.ExitCode,
            StdOut = rubyInstall.CombinedOutput,
            StdErr = (gem.StdErr + Environment.NewLine
                + "Ruby 似乎已安裝，但目前程序仍找不到 gem。請關閉並重新開啟 Jekyller 後再試一次。").Trim()
        };
    }

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
