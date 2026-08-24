using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Jekyller.Services;

public static partial class MarkdownPreviewService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseDefinitionLists()
        .UseEmojiAndSmiley()
        .UseAlertBlocks()
        .UseDiagrams()
        .UseMediaLinks()
        .UseCjkFriendlyEmphasis()
        .UseGlobalization()
        .Build();

    public static string StripFrontMatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var body = markdown;
        while (FrontMatterRegex().Match(body) is { Success: true } match)
            body = body[match.Length..].TrimStart('\r', '\n');
        return body;
    }

    public static string ExtractFrontMatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var m = FrontMatterRegex().Match(markdown);
        return m.Success ? m.Groups["frontMatter"].Value.Trim() : string.Empty;
    }

    public static string ToHtmlFragment(string markdown)
    {
        var bodyMd = StripFrontMatter(markdown);
        if (string.IsNullOrWhiteSpace(bodyMd))
            return string.Empty;
        return Markdown.ToHtml(bodyMd, Pipeline).Trim();
    }

    public static string ToHtmlDocument(string markdown, string? title = null)
    {
        var bodyMd = StripFrontMatter(markdown);
        var bodyHtml = Markdown.ToHtml(bodyMd, Pipeline);
        var pageTitle = string.IsNullOrWhiteSpace(title) ? "Preview" : title;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(pageTitle)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(DarkPreviewCss);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<article class=\"markdown-body\">");
        sb.AppendLine(bodyHtml);
        sb.AppendLine("</article></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Empty WebView shell for live preview. Call <c>jekyllerSetPreview(html)</c> to replace the article.
    /// </summary>
    public static string PreviewShellDocument()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data: https: http: file: blob:; media-src data: https: http: file: blob:;\"/>");
        sb.AppendLine("<title>Preview</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(DarkPreviewCss);
        sb.AppendLine("#placeholder { display:block; color:#6b7785; font-style:italic; padding:20px 24px; }");
        sb.AppendLine("#content { display:none; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p id=\"placeholder\">開始輸入 Markdown，預覽會即時更新。</p>");
        sb.AppendLine("<article id=\"content\" class=\"markdown-body\"></article>");
        sb.AppendLine("<script>");
        sb.AppendLine("window.jekyllerMedia = {};");
        sb.AppendLine("function jekyllerLookupMedia(src) {");
        sb.AppendLine("  if (!src) return null;");
        sb.AppendLine("  if (window.jekyllerMedia[src]) return window.jekyllerMedia[src];");
        sb.AppendLine("  try {");
        sb.AppendLine("    var decoded = decodeURI(src);");
        sb.AppendLine("    if (decoded !== src && window.jekyllerMedia[decoded]) return window.jekyllerMedia[decoded];");
        sb.AppendLine("  } catch (e) {}");
        sb.AppendLine("  return null;");
        sb.AppendLine("}");
        sb.AppendLine("function jekyllerApplyMedia(root) {");
        sb.AppendLine("  if (!root) return;");
        sb.AppendLine("  root.querySelectorAll('img[src], audio[src], video[src], source[src]').forEach(function (el) {");
        sb.AppendLine("    var src = el.getAttribute('src');");
        sb.AppendLine("    if (!src || /^(data:|blob:|https?:|file:)/i.test(src)) return;");
        sb.AppendLine("    var mapped = jekyllerLookupMedia(src);");
        sb.AppendLine("    if (!mapped) return;");
        sb.AppendLine("    el.setAttribute('src', mapped);");
        sb.AppendLine("  });");
        sb.AppendLine("}");
        sb.AppendLine("window.jekyllerSetPreview = function (html, media) {");
        sb.AppendLine("  if (media) {");
        sb.AppendLine("    var keys = Object.keys(media);");
        sb.AppendLine("    for (var i = 0; i < keys.length; i++) window.jekyllerMedia[keys[i]] = media[keys[i]];");
        sb.AppendLine("  }");
        sb.AppendLine("  var content = document.getElementById('content');");
        sb.AppendLine("  var placeholder = document.getElementById('placeholder');");
        sb.AppendLine("  var scroller = document.scrollingElement || document.documentElement;");
        sb.AppendLine("  var top = scroller ? scroller.scrollTop : 0;");
        sb.AppendLine("  var empty = !html;");
        sb.AppendLine("  placeholder.style.display = empty ? 'block' : 'none';");
        sb.AppendLine("  content.style.display = empty ? 'none' : 'block';");
        sb.AppendLine("  var wrap = document.createElement('div');");
        sb.AppendLine("  wrap.innerHTML = html || '';");
        sb.AppendLine("  jekyllerApplyMedia(wrap);");
        sb.AppendLine("  content.innerHTML = wrap.innerHTML;");
        sb.AppendLine("  if (scroller) scroller.scrollTop = top;");
        sb.AppendLine("};");
        sb.AppendLine("document.addEventListener('click', function (event) {");
        sb.AppendLine("  var link = event.target && event.target.closest ? event.target.closest('a[href]') : null;");
        sb.AppendLine("  if (link) event.preventDefault();");
        sb.AppendLine("});");
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    public static string ToPlainPreviewHint(string markdown)
    {
        var body = StripFrontMatter(markdown).Trim();
        if (body.Length == 0)
            return "（空白預覽）";
        return body.Length > 2000 ? body[..2000] + "\n…" : body;
    }

    private const string DarkPreviewCss = """
:root { color-scheme: dark; }
html, body {
  margin: 0; padding: 0;
  background: #0d1218;
  color: #e6edf3;
  font-family: "Segoe UI", "Microsoft JhengHei", sans-serif;
  font-size: 15px;
  line-height: 1.65;
}
.markdown-body { padding: 20px 24px 40px; max-width: 820px; }
h1, h2, h3, h4, h5, h6 { color: #7cdaf9; margin-top: 1.4em; margin-bottom: 0.5em; font-weight: 650; }
h1 { font-size: 1.9em; border-bottom: 1px solid #2a3648; padding-bottom: 0.25em; }
h2 { font-size: 1.5em; border-bottom: 1px solid #243041; padding-bottom: 0.2em; }
h3 { font-size: 1.25em; }
p { margin: 0.75em 0; }
a { color: #5ec8f0; text-decoration: none; }
a:hover { text-decoration: underline; }
code {
  font-family: Consolas, "Cascadia Mono", monospace;
  background: #1a2330;
  padding: 0.15em 0.4em;
  border-radius: 4px;
  font-size: 0.92em;
}
pre {
  background: #121a24;
  border: 1px solid #2a3648;
  border-radius: 8px;
  padding: 12px 14px;
  overflow: auto;
}
pre code { background: transparent; padding: 0; }
blockquote {
  margin: 1em 0;
  padding: 0.4em 1em;
  border-left: 4px solid #0e7490;
  background: #151c26;
  color: #c5d0dc;
}
.markdown-alert { margin: 1em 0; padding: 0.45em 1em; border-left: 4px solid #388bfd; background: #151c26; }
.markdown-alert-title { margin: 0 0 0.35em; color: #7cdaf9; font-weight: 650; }
table { border-collapse: collapse; width: 100%; margin: 1em 0; }
th, td { border: 1px solid #2a3648; padding: 8px 10px; }
th { background: #1a2330; }
img { max-width: 100%; height: auto; border-radius: 6px; }
.markdown-body::after { content: ""; display: table; clear: both; }
audio, video { width: 100%; max-width: 640px; margin: 1em 0; }
hr { border: none; border-top: 1px solid #2a3648; margin: 1.5em 0; }
ul, ol { padding-left: 1.4em; }
li { margin: 0.25em 0; }
li > input[type="checkbox"] { margin-right: 0.45em; }
strong { color: #fff; }
mark { color: #fff; background: #6e5a13; padding: 0.05em 0.2em; border-radius: 3px; }
ins { text-decoration: underline; }
dl { margin: 1em 0; }
dt { color: #fff; font-weight: 650; }
dd { margin: 0.25em 0 0.75em 1.5em; }
figure { margin: 1em 0; }
figcaption { color: #9aa7b5; font-size: 0.9em; text-align: center; }
.math { overflow-x: auto; }
.mermaid { padding: 12px 14px; border: 1px solid #2a3648; border-radius: 8px; background: #121a24; }
""";

    [GeneratedRegex(@"\A(?<delimiter>---|\+\+\+)(?:\s*\r?\n(?<frontMatter>.*?)\r?\n\k<delimiter>|\s+(?<frontMatter>.*?)\s+\k<delimiter>)\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
