using Jekyller.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Jekyller.Services;

public interface IThemeService
{
    IReadOnlyList<ThemeInfo> GetCatalog();
    Task<ProcessResult> InstallThemeAsync(
        string projectPath,
        ThemeInfo theme,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
    Task<string> DetectCurrentThemeAsync(string projectPath, CancellationToken cancellationToken = default);
}

public sealed class ThemeService(IProcessRunner processRunner, IConfigService configService) : IThemeService
{
    public IReadOnlyList<ThemeInfo> GetCatalog() =>
    [
        new ThemeInfo
        {
            Id = "chirpy",
            Name = "Chirpy",
            Description = "功能完整的響應式部落格主題，支援標籤、分類、搜尋與暗色模式。推薦用於 GitHub Pages。",
            Method = ThemeInstallMethod.GitClone,
            GitUrl = "https://github.com/cotes2020/chirpy-starter.git",
            RemoteTheme = "cotes2020/jekyll-theme-chirpy",
            DocsUrl = "https://github.com/cotes2020/jekyll-theme-chirpy",
            ConfigHints =
            [
                "title / description / url / baseurl",
                "avatar / social links",
                "theme_mode: light | dark | automatic"
            ]
        },
        new ThemeInfo
        {
            Id = "minima",
            Name = "Minima",
            Description = "Jekyll 官方預設主題，簡潔輕量。",
            Method = ThemeInstallMethod.Gem,
            GemName = "minima",
            DocsUrl = "https://github.com/jekyll/minima"
        },
        new ThemeInfo
        {
            Id = "minimal-mistakes",
            Name = "Minimal Mistakes",
            Description = "靈活的多用途主題，適合文件與個人站。",
            Method = ThemeInstallMethod.RemoteTheme,
            RemoteTheme = "mmistakes/minimal-mistakes",
            DocsUrl = "https://mmistakes.github.io/minimal-mistakes/"
        },
        new ThemeInfo
        {
            Id = "cayman",
            Name = "Cayman",
            Description = "GitHub Pages 官方主題之一，乾淨的專案首頁風格。",
            Method = ThemeInstallMethod.RemoteTheme,
            RemoteTheme = "pages-themes/cayman",
            DocsUrl = "https://pages-themes.github.io/cayman/"
        },
        new ThemeInfo
        {
            Id = "midnight",
            Name = "Midnight",
            Description = "GitHub Pages 官方暗色主題。",
            Method = ThemeInstallMethod.RemoteTheme,
            RemoteTheme = "pages-themes/midnight",
            DocsUrl = "https://pages-themes.github.io/midnight/"
        }
    ];

    public async Task<ProcessResult> InstallThemeAsync(
        string projectPath,
        ThemeInfo theme,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        return theme.Method switch
        {
            ThemeInstallMethod.Gem => await InstallGemThemeAsync(projectPath, theme, output, cancellationToken)
                .ConfigureAwait(false),
            ThemeInstallMethod.RemoteTheme => await InstallRemoteThemeAsync(projectPath, theme, output, cancellationToken)
                .ConfigureAwait(false),
            ThemeInstallMethod.GitClone => await InstallChirpyStarterAsync(projectPath, theme, output, cancellationToken)
                .ConfigureAwait(false),
            _ => new ProcessResult { ExitCode = 1, StdErr = "未知的主題安裝方式" }
        };
    }

    public async Task<string> DetectCurrentThemeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var cfg = await configService.LoadAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cfg.RemoteTheme))
            return $"remote_theme: {cfg.RemoteTheme}";
        if (!string.IsNullOrWhiteSpace(cfg.Theme))
            return $"theme: {cfg.Theme}";

        var gemfile = Path.Combine(projectPath, "Gemfile");
        if (File.Exists(gemfile))
        {
            var text = await File.ReadAllTextAsync(gemfile, cancellationToken).ConfigureAwait(false);
            var match = Regex.Match(text, @"gem\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups[1].Value.Contains("theme", StringComparison.OrdinalIgnoreCase))
                return match.Groups[1].Value;
            if (text.Contains("jekyll-theme-chirpy", StringComparison.OrdinalIgnoreCase))
                return "jekyll-theme-chirpy";
        }

        return "（未偵測到主題設定）";
    }

    private async Task<ProcessResult> InstallGemThemeAsync(
        string projectPath,
        ThemeInfo theme,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(theme.GemName))
            return new ProcessResult { ExitCode = 1, StdErr = "缺少 gem 名稱" };

        await EnsureGemfileAsync(projectPath, theme.GemName, cancellationToken).ConfigureAwait(false);
        await configService.SaveFieldsAsync(projectPath, new JekyllConfigSnapshot
        {
            Theme = theme.GemName,
            RemoteTheme = string.Empty
        }, cancellationToken).ConfigureAwait(false);

        // Clear remote_theme if present
        await PatchConfigRemoveKeyAsync(projectPath, "remote_theme", cancellationToken).ConfigureAwait(false);
        await PatchConfigSetKeyAsync(projectPath, "theme", theme.GemName, cancellationToken).ConfigureAwait(false);

        output?.Report($"已設定 theme: {theme.GemName}，執行 bundle install…");
        return await processRunner.RunAsync("bundle", "install", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProcessResult> InstallRemoteThemeAsync(
        string projectPath,
        ThemeInfo theme,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(theme.RemoteTheme))
            return new ProcessResult { ExitCode = 1, StdErr = "缺少 remote_theme" };

        await EnsurePluginAsync(projectPath, "jekyll-remote-theme", cancellationToken).ConfigureAwait(false);
        await PatchConfigRemoveKeyAsync(projectPath, "theme", cancellationToken).ConfigureAwait(false);
        await PatchConfigSetKeyAsync(projectPath, "remote_theme", theme.RemoteTheme, cancellationToken)
            .ConfigureAwait(false);
        await EnsurePluginsListHasAsync(projectPath, "jekyll-remote-theme", cancellationToken).ConfigureAwait(false);

        output?.Report($"已設定 remote_theme: {theme.RemoteTheme}，執行 bundle install…");
        return await processRunner.RunAsync("bundle", "install", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProcessResult> InstallChirpyStarterAsync(
        string projectPath,
        ThemeInfo theme,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        // Prefer remote_theme approach for existing sites; use starter clone into empty/new folder.
        var hasContent = Directory.EnumerateFileSystemEntries(projectPath).Any();
        if (!hasContent && !string.IsNullOrWhiteSpace(theme.GitUrl))
        {
            output?.Report("以 chirpy-starter 範本初始化空專案…");
            var parent = Path.GetDirectoryName(projectPath)!;
            var name = Path.GetFileName(projectPath);
            var tmp = Path.Combine(parent, $".jekyller-chirpy-{Guid.NewGuid():N}");

            var clone = await processRunner.RunAsync(
                "git",
                $"clone --depth 1 \"{theme.GitUrl}\" \"{tmp}\"",
                parent,
                output,
                cancellationToken).ConfigureAwait(false);

            if (!clone.Success)
                return clone;

            foreach (var entry in Directory.EnumerateFileSystemEntries(tmp))
            {
                var dest = Path.Combine(projectPath, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                    Directory.Move(entry, dest);
                else
                    File.Move(entry, dest);
            }

            try { Directory.Delete(tmp, true); } catch { /* ignore */ }

            // Remove nested .git so user can init their own repo
            var nestedGit = Path.Combine(projectPath, ".git");
            if (Directory.Exists(nestedGit))
            {
                try { Directory.Delete(nestedGit, true); } catch { /* ignore */ }
            }

            return await processRunner.RunAsync("bundle", "install", projectPath, output, cancellationToken)
                .ConfigureAwait(false);
        }

        // Existing site: wire remote_theme + chirpy gem preference via Gemfile
        output?.Report("既有專案：設定 jekyll-theme-chirpy / remote_theme…");
        await EnsureGemfileAsync(projectPath, "jekyll-theme-chirpy", cancellationToken).ConfigureAwait(false);
        await EnsurePluginAsync(projectPath, "jekyll-remote-theme", cancellationToken).ConfigureAwait(false);
        await PatchConfigRemoveKeyAsync(projectPath, "theme", cancellationToken).ConfigureAwait(false);
        await PatchConfigSetKeyAsync(projectPath, "remote_theme", theme.RemoteTheme ?? "cotes2020/jekyll-theme-chirpy", cancellationToken)
            .ConfigureAwait(false);
        await EnsurePluginsListHasAsync(projectPath, "jekyll-remote-theme", cancellationToken).ConfigureAwait(false);

        return await processRunner.RunAsync("bundle", "install", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureGemfileAsync(string projectPath, string gemName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectPath, "Gemfile");
        if (!File.Exists(path))
        {
            var content = $"""
                           source "https://rubygems.org"

                           gem "jekyll", "~> 4.3"
                           gem "{gemName}"
                           gem "jekyll-remote-theme"
                           """;
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!text.Contains(gemName, StringComparison.OrdinalIgnoreCase))
        {
            text = text.TrimEnd() + Environment.NewLine + $"gem \"{gemName}\"" + Environment.NewLine;
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsurePluginAsync(string projectPath, string gemName, CancellationToken cancellationToken)
    {
        await EnsureGemfileAsync(projectPath, gemName, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePluginsListHasAsync(string projectPath, string plugin, CancellationToken cancellationToken)
    {
        var path = configService.FindConfigPath(projectPath);
        if (path is null)
            return;

        var raw = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        if (raw.Contains(plugin, StringComparison.OrdinalIgnoreCase))
            return;

        if (Regex.IsMatch(raw, @"(?m)^plugins\s*:"))
        {
            // Append under plugins list if simple dash list exists.
            raw = Regex.Replace(
                raw,
                @"(?ms)^(plugins\s*:\s*\n(?:\s*-\s*.*\n)*)",
                m => m.Groups[1].Value + $"  - {plugin}{Environment.NewLine}");
        }
        else
        {
            raw = raw.TrimEnd() + Environment.NewLine + "plugins:" + Environment.NewLine +
                  $"  - {plugin}" + Environment.NewLine;
        }

        await File.WriteAllTextAsync(path, raw, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private async Task PatchConfigSetKeyAsync(string projectPath, string key, string value, CancellationToken cancellationToken)
    {
        var cfg = await configService.LoadAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var fields = new JekyllConfigSnapshot
        {
            Title = cfg.Title,
            Description = cfg.Description,
            Url = cfg.Url,
            BaseUrl = cfg.BaseUrl,
            Email = cfg.Email,
            Theme = key == "theme" ? value : cfg.Theme,
            RemoteTheme = key == "remote_theme" ? value : cfg.RemoteTheme,
            Lang = cfg.Lang,
            Timezone = cfg.Timezone,
            Markdown = cfg.Markdown,
            Permalink = cfg.Permalink
        };
        await configService.SaveFieldsAsync(projectPath, fields, cancellationToken).ConfigureAwait(false);
    }

    private async Task PatchConfigRemoveKeyAsync(string projectPath, string key, CancellationToken cancellationToken)
    {
        var path = configService.FindConfigPath(projectPath);
        if (path is null || !File.Exists(path))
            return;

        var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var pattern = new Regex($@"(?m)^{Regex.Escape(key)}\s*:.*(?:\r?\n)?");
        raw = pattern.Replace(raw, string.Empty);
        await File.WriteAllTextAsync(path, raw, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }
}
