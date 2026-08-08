namespace Jekyller.Models;

public sealed class ToolStatus
{
    public required string Name { get; init; }
    public bool IsInstalled { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public string Display => IsInstalled
        ? $"{Name} ✓ {Version}"
        : $"{Name} ✗ 未安裝";
}
