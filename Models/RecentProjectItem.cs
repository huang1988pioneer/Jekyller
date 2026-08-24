namespace Jekyller.Models;

public sealed class RecentProjectItem
{
    public string Name { get; }
    public string FolderPath { get; }
    public bool IsCurrent { get; }

    public RecentProjectItem(string folderPath, bool isCurrent)
    {
        FolderPath = folderPath;
        IsCurrent = isCurrent;
        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        Name = string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }
}
