using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Jekyller.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Jekyller.Services;

public interface IMarkdownPreviewService
{
    string ExtractBody(string? fullDocument);
    string ToHtml(string? markdownBody);
    IReadOnlyList<PreviewBlock> ToBlocks(string? markdownBody);
}

public sealed class MarkdownPreviewService : IMarkdownPreviewService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Regex FrontMatter = new(
        @"^---\s*\r?\n.*?\r?\n---\s*\r?\n?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public string ExtractBody(string? fullDocument)
    {
        if (string.IsNullOrEmpty(fullDocument))
            return string.Empty;

        var text = fullDocument.Replace("\r\n", "\n");
        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            var stripped = FrontMatter.Replace(text, string.Empty, 1);
            return stripped.TrimStart('\n');
        }

        return fullDocument;
    }

    public string ToHtml(string? markdownBody)
    {
        var body = markdownBody ?? string.Empty;
        var html = Markdig.Markdown.ToHtml(body, Pipeline);
        return $$"""
                 <!DOCTYPE html>
                 <html>
                 <head>
                   <meta charset="utf-8" />
                   <style>
                     body { font-family: Segoe UI, system-ui, sans-serif; padding: 16px; line-height: 1.6;
                            background: #1e1e1e; color: #e8e8e8; max-width: 900px; margin: 0 auto; }
                     pre, code { background: #2d2d2d; border-radius: 4px; font-family: Consolas, monospace; }
                     pre { padding: 12px; overflow: auto; }
                     code { padding: 2px 4px; }
                     a { color: #7aa2ff; }
                     h1,h2,h3 { border-bottom: 1px solid #333; padding-bottom: 4px; }
                     blockquote { border-left: 4px solid #555; margin-left: 0; padding-left: 12px; color: #bbb; }
                     table { border-collapse: collapse; }
                     th, td { border: 1px solid #444; padding: 6px 10px; }
                     img { max-width: 100%; }
                   </style>
                 </head>
                 <body>{{html}}</body>
                 </html>
                 """;
    }

    public IReadOnlyList<PreviewBlock> ToBlocks(string? markdownBody)
    {
        var body = markdownBody ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var doc = Markdig.Markdown.Parse(body, Pipeline);
        var blocks = new List<PreviewBlock>();

        foreach (var block in doc)
            AppendBlock(block, blocks);

        if (blocks.Count == 0)
        {
            // Fallback: plain paragraphs
            foreach (var line in body.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    blocks.Add(new PreviewBlock { Kind = "p", Text = line.TrimEnd('\r') });
            }
        }

        return blocks;
    }

    private static void AppendBlock(Block block, List<PreviewBlock> blocks)
    {
        switch (block)
        {
            case HeadingBlock h:
                var level = Math.Clamp(h.Level, 1, 3);
                blocks.Add(new PreviewBlock
                {
                    Kind = "h" + level,
                    Text = InlineToText(h.Inline)
                });
                break;
            case ParagraphBlock p:
                blocks.Add(new PreviewBlock { Kind = "p", Text = InlineToText(p.Inline) });
                break;
            case CodeBlock code:
                var lines = code is FencedCodeBlock f
                    ? string.Join(Environment.NewLine, f.Lines.Lines.Select(l => l.ToString()).Where(s => s is not null))
                    : code.Lines.ToString();
                blocks.Add(new PreviewBlock { Kind = "code", Text = lines?.TrimEnd() ?? string.Empty });
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                    AppendBlock(child, blocks);
                if (blocks.Count > 0 && blocks[^1].Kind == "p")
                {
                    var last = blocks[^1];
                    blocks[^1] = new PreviewBlock { Kind = "quote", Text = last.Text };
                }
                break;
            case ListBlock list:
                foreach (var item in list)
                {
                    if (item is not ListItemBlock li) continue;
                    var text = string.Join(" ", li.OfType<ParagraphBlock>().Select(pb => InlineToText(pb.Inline)));
                    blocks.Add(new PreviewBlock { Kind = "li", Text = "• " + text });
                }
                break;
            case ThematicBreakBlock:
                blocks.Add(new PreviewBlock { Kind = "hr", Text = "—" });
                break;
            case HtmlBlock html:
                blocks.Add(new PreviewBlock { Kind = "p", Text = StripHtml(html.Lines.ToString() ?? string.Empty) });
                break;
        }
    }

    private static string InlineToText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var child in inline)
        {
            switch (child)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append('`').Append(code.Content).Append('`');
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case LinkInline link:
                    sb.Append(InlineToText(link));
                    if (!string.IsNullOrEmpty(link.Url))
                        sb.Append(" (").Append(link.Url).Append(')');
                    break;
                case EmphasisInline em:
                    sb.Append(InlineToText(em));
                    break;
                case ContainerInline container:
                    sb.Append(InlineToText(container));
                    break;
                default:
                    if (child is LeafInline leaf)
                        sb.Append(leaf.ToString());
                    break;
            }
        }

        return sb.ToString();
    }

    private static string StripHtml(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return HtmlEntity.DeEntitize(doc.DocumentNode.InnerText).Trim();
        }
        catch
        {
            return html;
        }
    }
}
