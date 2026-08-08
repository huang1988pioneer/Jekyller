namespace Jekyller.Models;

public sealed class PreviewBlock
{
    public required string Kind { get; init; } // h1,h2,h3,p,code,li,quote,hr
    public required string Text { get; init; }
}
