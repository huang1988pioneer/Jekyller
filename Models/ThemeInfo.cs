namespace Jekyller.Models;

public sealed class ThemeInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ThemeInstallMethod Method { get; init; }
    public string? GemName { get; init; }
    public string? RemoteTheme { get; init; }
    public string? GitUrl { get; init; }
    public string DocsUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> ConfigHints { get; init; } = [];
}

public enum ThemeInstallMethod
{
    Gem,
    RemoteTheme,
    GitClone
}
