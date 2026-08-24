namespace Jekyller.Models;

public sealed class JekyllVersionCheckResult
{
    public string InstalledVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public bool IsInstalled { get; init; }
    public bool CheckSucceeded { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string Message { get; init; } = string.Empty;
}
