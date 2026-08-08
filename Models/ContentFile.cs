namespace Jekyller.Models;

public sealed class ContentFile
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public required ContentKind Kind { get; init; }
    public DateTime LastWriteTime { get; init; }

    public override string ToString() => RelativePath;
}

public enum ContentKind
{
    Post,
    Page,
    Draft,
    Other
}
