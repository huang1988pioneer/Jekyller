using Jekyller.Services;

var heading = RoundTrip("# 標題\n\n段落文字");
AssertContains(heading, "# 標題");
AssertContains(heading, "段落文字");

var emphasis = RoundTrip("這是 **粗體**、*斜體* 與 ~~刪除~~。");
AssertContains(emphasis, "**粗體**");
AssertContains(emphasis, "*斜體*");
AssertContains(emphasis, "~~刪除~~");

var emphasisExtras = RoundTrip("H~2~O、x^2^、==標示==、++插入++");
AssertContains(emphasisExtras, "~2~");
AssertContains(emphasisExtras, "^2^");
AssertContains(emphasisExtras, "==標示==");
AssertContains(emphasisExtras, "++插入++");

var advancedHtml = MarkdownPreviewService.ToHtmlFragment("""
> [!NOTE]
> 提示內容

縮寫 HTML

*[HTML]: Hyper Text Markup Language

術語
:    定義

行內數學 $x^2$ 與 :smile:
""");
AssertContains(advancedHtml, "markdown-alert");
AssertContains(advancedHtml, "<abbr");
AssertContains(advancedHtml, "<dl>");
AssertContains(advancedHtml, "class=\"math\"");
AssertContains(advancedHtml, "😄");

var liquid = RoundTrip("開頭\n\n{% include figure.html src=\"a.jpg\" %}\n\n{{ page.title }}\n\n結尾");
AssertContains(liquid, "{% include figure.html src=\"a.jpg\" %}");
AssertContains(liquid, "{{ page.title }}");

var frontMatter = """
---
title: Demo
published: false
---

正文 **Hello**
""";
var html = MarkdownWysiwygConverter.ToEditableHtml(frontMatter);
Assert(!html.Contains("title: Demo", StringComparison.Ordinal), "WYSIWYG HTML must not include front matter.");
var body = MarkdownWysiwygConverter.FromEditableHtml(html);
AssertContains(body, "**Hello**");
Assert(!body.Contains("title: Demo", StringComparison.Ordinal), "Converted markdown must stay body-only.");

var document = new FrontMatterService().Parse(frontMatter);
var rejoined = new FrontMatterService().ReplaceBody(frontMatter, body);
Assert(rejoined.Contains("title:", StringComparison.Ordinal), "Rejoin must keep YAML.");
Assert(rejoined.Contains("**Hello**", StringComparison.Ordinal), "Rejoin must keep converted body.");
Assert(document.Fields["title"] == "Demo", "Front matter parse still works.");

var emptyHtml = MarkdownWysiwygConverter.ToEditableHtml("""
---
title: Empty
---

""");
Assert(emptyHtml.Contains("<p>", StringComparison.Ordinal), "empty body still gets an editable paragraph");
Assert(string.IsNullOrWhiteSpace(MarkdownWysiwygConverter.FromEditableHtml(emptyHtml)), "empty paragraph converts back to blank markdown");
Assert(string.IsNullOrWhiteSpace(MarkdownWysiwygConverter.FromEditableHtml("<p><br></p>")), "placeholder paragraph is blank markdown");

var imageOnly = MarkdownWysiwygConverter.FromEditableHtml(
    """<p><br></p><p><img src="/assets/img/a.png" alt="penguin"></p><p><br></p>""");
AssertContains(imageOnly, "/assets/img/a.png");
Assert(!imageOnly.Contains("開始撰寫", StringComparison.Ordinal), "typing placeholders must not leak into markdown");

Console.WriteLine("MARKDOWN_WYSIWYG_HARNESS_OK");

static string RoundTrip(string markdown)
{
    var html = MarkdownWysiwygConverter.ToEditableHtml(markdown);
    return MarkdownWysiwygConverter.FromEditableHtml(html);
}

static void AssertContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected to contain {expected}. Got:\n{text}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
