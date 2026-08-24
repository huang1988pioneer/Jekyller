using Jekyller.Services;

var yaml = """
---
layout: post
title: "Hello World"
date: 2026-08-23 10:00:00 +08:00
categories: [news, jekyll]
tags: [desktop]
image:
  path: /assets/img/cover.png
  alt: Cover
---

正文段落
""";

var duplicated = """
---
title: "Outer"
---

---
title: "Inner"
date: 2026-08-23 10:00:00 +08:00
---

Ox Alpha
""";

var service = new FrontMatterService();
var document = service.Parse(yaml);
Assert(document.Fields["title"] == "Hello World", "YAML title must be parsed.");
Assert(document.Fields["layout"] == "post", "layout must be parsed.");
Assert(document.Fields["image"] == "/assets/img/cover.png", "Nested image.path must flatten to image.");
Assert(document.Fields["image.alt"] == "Cover", "Nested image.alt must be preserved.");
Assert(document.Body.Trim() == "正文段落", "YAML delimiters must not leak into the body.");

var rewritten = service.Parse(service.Write(document));
Assert(rewritten.Fields["image"] == "/assets/img/cover.png", "Written image.path must round-trip.");
Assert(rewritten.Fields["categories"].Contains("news", StringComparison.Ordinal), "categories must round-trip.");
Assert(rewritten.Body.Trim() == "正文段落", "Body must survive write.");

var mixedDocument = service.Parse(duplicated);
Assert(mixedDocument.Fields["title"] == "Outer", "Outer title must win when cleaning duplicated front matter.");
Assert(mixedDocument.Fields["date"] == "2026-08-23 10:00:00 +08:00", "Inner date should be recovered.");
Assert(mixedDocument.Body.Trim() == "Ox Alpha", "Duplicated front matter must be stripped from body.");

var previewBody = MarkdownPreviewService.StripFrontMatter(duplicated).Trim();
Assert(previewBody == "Ox Alpha", $"Preview body should only contain Markdown content, got: {previewBody}");

var replaced = service.ReplaceBody(yaml, "新的正文");
Assert(replaced.Contains("title: \"Hello World\"", StringComparison.Ordinal)
       || replaced.Contains("title: Hello World", StringComparison.Ordinal),
    "ReplaceBody must keep YAML.");
Assert(replaced.Contains("新的正文", StringComparison.Ordinal), "ReplaceBody must insert the new body.");
Assert(!replaced.Contains("正文段落", StringComparison.Ordinal), "ReplaceBody must drop the old body.");

Console.WriteLine("FRONT_MATTER_FORMATS_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
