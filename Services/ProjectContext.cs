namespace Jekyller.Services;

public interface IProjectContext
{
    string? ProjectPath { get; }
    bool HasProject { get; }
    event EventHandler? ProjectChanged;
    void SetProject(string? path);
}

public sealed class ProjectContext : IProjectContext
{
    public string? ProjectPath { get; private set; }
    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath) && Directory.Exists(ProjectPath);

    public event EventHandler? ProjectChanged;

    public void SetProject(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path.Trim());

        if (string.Equals(ProjectPath, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        ProjectPath = normalized;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}
