namespace Jekyller.Services;

public interface IJekyllService
{
    Task<ProcessResult> CreateSiteAsync(
        string parentDirectory,
        string siteName,
        bool force,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> BundleInstallAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> ServeAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> BuildAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        bool production = false);

    bool LooksLikeJekyllSite(string path);
}

public sealed class JekyllService(IProcessRunner processRunner) : IJekyllService
{
    public async Task<ProcessResult> CreateSiteAsync(
        string parentDirectory,
        string siteName,
        bool force,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(parentDirectory);
        var args = $"new \"{siteName}\"";
        if (force)
            args += " --force";

        var result = await processRunner.RunAsync(
            "jekyll",
            args,
            parentDirectory,
            output,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return result;

        var sitePath = Path.Combine(parentDirectory, siteName);
        if (Directory.Exists(sitePath))
        {
            var bundle = await BundleInstallAsync(sitePath, output, cancellationToken).ConfigureAwait(false);
            if (!bundle.Success)
            {
                return new ProcessResult
                {
                    ExitCode = bundle.ExitCode,
                    StdOut = result.CombinedOutput + Environment.NewLine + bundle.StdOut,
                    StdErr = bundle.StdErr
                };
            }
        }

        return result;
    }

    public Task<ProcessResult> BundleInstallAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
        => processRunner.RunAsync("bundle", "install", projectPath, output, cancellationToken);

    public async Task<ProcessResult> ServeAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var clean = JekyllWorkspace.CleanGeneratedCaches(projectPath, output);
        if (clean is not null)
            return clean;

        return await processRunner.RunAsync("bundle", "exec jekyll serve --livereload", projectPath, output, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProcessResult> BuildAsync(
        string projectPath,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default,
        bool production = false)
    {
        var clean = JekyllWorkspace.CleanGeneratedCaches(projectPath, output);
        if (clean is not null)
            return clean;

        IReadOnlyDictionary<string, string?>? env = production
            ? new Dictionary<string, string?> { ["JEKYLL_ENV"] = "production" }
            : null;

        return await processRunner.RunAsync(
                "bundle",
                "exec jekyll build",
                projectPath,
                output,
                cancellationToken,
                env)
            .ConfigureAwait(false);
    }

    public bool LooksLikeJekyllSite(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "_config.yml"))
               || File.Exists(Path.Combine(path, "_config.yaml"))
               || Directory.Exists(Path.Combine(path, "_posts"))
               || File.Exists(Path.Combine(path, "Gemfile"));
    }
}
