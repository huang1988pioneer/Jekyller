using Jekyller.Models;
using Jekyller.Services;

var root = Path.Combine(Path.GetTempPath(), "JekyllerSiteMigrationTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var frontMatter = new FrontMatterService();
var migration = new SiteMigrationService();

try
{
    var jekyllSite = Path.Combine(root, "jekyll");
    WriteJekyllSite(jekyllSite);
    Assert(StaticSiteDetector.Detect(jekyllSite) == StaticSiteKind.Jekyll, "Jekyll site must be detected.");
    Assert(StaticSiteDetector.LooksLikeJekyll(jekyllSite), "LooksLikeJekyll must accept a Jekyll root.");
    Assert(!StaticSiteDetector.LooksLikeHugo(jekyllSite), "Jekyll must not look like Hugo.");
    Assert(!StaticSiteDetector.LooksLikeHexo(jekyllSite), "Jekyll must not look like Hexo.");

    var hugoSite = Path.Combine(root, "hugo");
    WriteHugoSite(hugoSite);
    Assert(StaticSiteDetector.Detect(hugoSite) == StaticSiteKind.Hugo, "Hugo site must be detected.");

    var hexoSite = Path.Combine(root, "hexo");
    WriteHexoSite(hexoSite);
    Assert(StaticSiteDetector.Detect(hexoSite) == StaticSiteKind.Hexo, "Hexo site must be detected.");
    Assert(!StaticSiteDetector.LooksLikeJekyll(hexoSite), "Hexo _config.yml must not be classified as Jekyll.");

    var jekyllPost = File.ReadAllText(Path.Combine(jekyllSite, "_posts", "2026-08-23-hello.md"));
    var hugoArticle = ArticleFormatConverter.Convert(
        jekyllPost, "2026-08-23-hello.md", "_posts/2026-08-23-hello.md",
        ContentKind.Post, StaticSiteKind.Jekyll, StaticSiteKind.Hugo);
    Assert(hugoArticle.RelativePath.StartsWith("content/posts/", StringComparison.Ordinal),
        "Hugo posts must go under content/posts.");
    var hugoDoc = frontMatter.Parse(hugoArticle.Markdown);
    Assert(hugoDoc.Fields["title"] == "Hello World", "Hugo export must keep the title.");
    Assert(hugoDoc.Fields.ContainsKey("draft") && hugoDoc.Fields["draft"] == "true",
        "Jekyll published: false must become Hugo draft: true.");
    Assert(!hugoDoc.Fields.ContainsKey("layout"), "Hugo export must drop Jekyll layout.");
    Assert(hugoDoc.Fields["tags"].Contains("desktop", StringComparison.Ordinal), "Hugo export must keep tags.");
    Assert(hugoArticle.Markdown.Contains("```csharp", StringComparison.Ordinal),
        "Jekyll highlight tags must become fenced code for Hugo.");
    Assert(!hugoArticle.Markdown.Contains("{% highlight", StringComparison.Ordinal),
        "Jekyll highlight tags must be removed.");
    Assert(hugoArticle.Markdown.Contains("![Cover](/assets/img/cover.png)", StringComparison.Ordinal)
           || hugoDoc.Fields["image"].Contains("/assets/img/cover.png", StringComparison.Ordinal),
        "Cover image must survive Hugo export.");

    var hexoArticle = ArticleFormatConverter.Convert(
        jekyllPost, "2026-08-23-hello.md", "_posts/2026-08-23-hello.md",
        ContentKind.Post, StaticSiteKind.Jekyll, StaticSiteKind.Hexo);
    Assert(hexoArticle.RelativePath.StartsWith("source/_drafts/", StringComparison.Ordinal)
           || hexoArticle.RelativePath.StartsWith("source/_posts/", StringComparison.Ordinal),
        "Hexo posts must go under source/_posts or source/_drafts.");
    var hexoDoc = frontMatter.Parse(hexoArticle.Markdown);
    Assert(hexoDoc.Fields["title"] == "Hello World", "Hexo export must keep the title.");
    Assert(hexoDoc.Fields["published"] == "false", "Hexo export must keep unpublished as published: false.");
    Assert(!hexoDoc.Fields.ContainsKey("layout"), "Hexo export must drop Jekyll layout.");

    var hugoToJekyll = ArticleFormatConverter.Convert(
        File.ReadAllText(Path.Combine(hugoSite, "content", "posts", "hello.md")),
        "hello.md", "content/posts/hello.md",
        ContentKind.Post, StaticSiteKind.Hugo, StaticSiteKind.Jekyll);
    Assert(hugoToJekyll.RelativePath.StartsWith("_posts/", StringComparison.Ordinal),
        "Hugo posts must become Jekyll _posts.");
    var fromHugo = frontMatter.Parse(hugoToJekyll.Markdown);
    Assert(fromHugo.Fields["layout"] == "post", "Hugo → Jekyll must add layout: post.");
    Assert(fromHugo.Fields["title"] == "Hugo Hello", "Hugo TOML title must be parsed.");
    Assert(fromHugo.Fields["categories"].Contains("news", StringComparison.Ordinal), "Hugo categories must round-trip.");
    Assert(hugoToJekyll.Markdown.Contains("![Sky](/img/sky.png)", StringComparison.Ordinal),
        "Hugo figure shortcode must become a Markdown image.");

    var hexoToJekyll = ArticleFormatConverter.Convert(
        File.ReadAllText(Path.Combine(hexoSite, "source", "_posts", "2026-01-02-hexo-hello.md")),
        "2026-01-02-hexo-hello.md", "source/_posts/2026-01-02-hexo-hello.md",
        ContentKind.Post, StaticSiteKind.Hexo, StaticSiteKind.Jekyll);
    var fromHexo = frontMatter.Parse(hexoToJekyll.Markdown);
    Assert(fromHexo.Fields["title"] == "Hexo Hello", "Hexo title must be parsed.");
    Assert(fromHexo.Fields["categories"].Contains("news", StringComparison.Ordinal),
        "Hexo dash-list categories must be imported.");
    Assert(hexoToJekyll.Markdown.Contains("![alt text](foo.png)", StringComparison.Ordinal),
        "Hexo asset_img must become a Markdown image.");

    var hugoDest = Path.Combine(root, "jekyll-to-hugo");
    var hugoResult = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = jekyllSite,
        DestinationPath = hugoDest,
        SourceKind = StaticSiteKind.Jekyll,
        TargetKind = StaticSiteKind.Hugo
    });
    Assert(hugoResult.Success, "Jekyll → Hugo migration must succeed: " + hugoResult.Message);
    Assert(hugoResult.Posts + hugoResult.Drafts >= 1, "Jekyll → Hugo must convert posts.");
    Assert(File.Exists(Path.Combine(hugoDest, "hugo.toml")), "Hugo target must write hugo.toml.");
    Assert(Directory.Exists(Path.Combine(hugoDest, "content", "posts")), "Hugo target must have content/posts.");
    Assert(File.Exists(Path.Combine(hugoDest, "static", "assets", "img", "cover.png")),
        "Jekyll assets must copy to Hugo static/assets.");
    Assert(StaticSiteDetector.Detect(hugoDest) == StaticSiteKind.Hugo, "Migrated Hugo folder must detect as Hugo.");

    var hexoDest = Path.Combine(root, "jekyll-to-hexo");
    var hexoResult = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = jekyllSite,
        DestinationPath = hexoDest,
        SourceKind = StaticSiteKind.Jekyll,
        TargetKind = StaticSiteKind.Hexo
    });
    Assert(hexoResult.Success, "Jekyll → Hexo migration must succeed: " + hexoResult.Message);
    Assert(File.Exists(Path.Combine(hexoDest, "_config.yml")), "Hexo target must write _config.yml.");
    Assert(File.Exists(Path.Combine(hexoDest, "package.json")), "Hexo target must write package.json.");
    Assert(StaticSiteDetector.Detect(hexoDest) == StaticSiteKind.Hexo, "Migrated Hexo folder must detect as Hexo.");

    var fromHugoDest = Path.Combine(root, "hugo-to-jekyll");
    var fromHugoResult = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = hugoSite,
        DestinationPath = fromHugoDest,
        SourceKind = StaticSiteKind.Hugo,
        TargetKind = StaticSiteKind.Jekyll
    });
    Assert(fromHugoResult.Success, "Hugo → Jekyll migration must succeed: " + fromHugoResult.Message);
    Assert(fromHugoResult.Posts >= 1, "Hugo → Jekyll must convert posts.");
    Assert(File.Exists(Path.Combine(fromHugoDest, "_config.yml")), "Jekyll target must write _config.yml.");
    Assert(Directory.Exists(Path.Combine(fromHugoDest, "_posts")), "Jekyll target must have _posts.");
    Assert(StaticSiteDetector.Detect(fromHugoDest) == StaticSiteKind.Jekyll,
        "Migrated Jekyll folder must detect as Jekyll.");
    var importedHugo = Directory.EnumerateFiles(Path.Combine(fromHugoDest, "_posts"), "*.md").First();
    var importedHugoDoc = frontMatter.Parse(File.ReadAllText(importedHugo));
    Assert(importedHugoDoc.Fields["title"] == "Hugo Hello", "Hugo post title must be imported.");
    Assert(importedHugoDoc.Fields["layout"] == "post", "Imported Hugo post must have layout.");

    var fromHexoDest = Path.Combine(root, "hexo-to-jekyll");
    var fromHexoResult = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = hexoSite,
        DestinationPath = fromHexoDest,
        SourceKind = StaticSiteKind.Hexo,
        TargetKind = StaticSiteKind.Jekyll
    });
    Assert(fromHexoResult.Success, "Hexo → Jekyll migration must succeed: " + fromHexoResult.Message);
    Assert(fromHexoResult.Posts >= 1, "Hexo → Jekyll must convert posts.");
    var importedHexo = Directory.EnumerateFiles(Path.Combine(fromHexoDest, "_posts"), "*.md").First();
    var importedHexoText = File.ReadAllText(importedHexo);
    Assert(importedHexoText.Contains("Hexo Hello", StringComparison.Ordinal), "Hexo post title must be imported.");
    Assert(importedHexoText.Contains("news", StringComparison.Ordinal), "Hexo categories must be imported.");

    var hugoPostFiles = Directory.Exists(Path.Combine(hugoDest, "content", "posts"))
        ? Directory.EnumerateFiles(Path.Combine(hugoDest, "content", "posts"), "*.md", SearchOption.AllDirectories).ToList()
        : [];
    Assert(hugoPostFiles.Count > 0,
        "Jekyll → Hugo should write Markdown under content/posts. Files: "
        + string.Join(", ", Directory.EnumerateFiles(hugoDest, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(hugoDest, file))));

    var roundTrip = Path.Combine(root, "round-trip-jekyll");
    var roundTripResult = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = hugoDest,
        DestinationPath = roundTrip,
        SourceKind = StaticSiteKind.Hugo,
        TargetKind = StaticSiteKind.Jekyll
    });
    Assert(roundTripResult.Success, "Hugo (from Jekyll) → Jekyll round-trip must succeed.");
    Assert(roundTripResult.Posts + roundTripResult.Drafts >= 1,
        "Round-trip should convert posts. " + roundTripResult.Summary + " Hugo posts: "
        + string.Join(", ", hugoPostFiles.Select(Path.GetFileName)));
    var roundTripPost = FirstMarkdown(Path.Combine(roundTrip, "_posts"), Path.Combine(roundTrip, "_drafts"))
        ?? throw new InvalidOperationException("Round-trip Jekyll site must contain a post or draft.");
    var roundTripText = File.ReadAllText(roundTripPost);
    Assert(roundTripText.Contains("Hello World", StringComparison.Ordinal),
        "Title must survive Jekyll → Hugo → Jekyll.");

    var unsupported = await migration.MigrateAsync(new SiteMigrationRequest
    {
        SourcePath = hugoSite,
        DestinationPath = Path.Combine(root, "nope"),
        SourceKind = StaticSiteKind.Hugo,
        TargetKind = StaticSiteKind.Hexo
    });
    Assert(!unsupported.Success, "Hugo → Hexo must be rejected.");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

Console.WriteLine("SITE_MIGRATION_HARNESS_OK");

static void WriteJekyllSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "_posts"));
    Directory.CreateDirectory(Path.Combine(path, "assets", "img"));
    File.WriteAllText(Path.Combine(path, "_config.yml"),
        """
        title: Jekyll Sample
        description: A demo
        url: https://example.com
        baseurl: /blog
        """);
    File.WriteAllText(Path.Combine(path, "_posts", "2026-08-23-hello.md"),
        """
        ---
        layout: post
        title: "Hello World"
        date: 2026-08-23 10:00:00 +08:00
        published: false
        categories: [news, jekyll]
        tags: [desktop]
        image:
          path: /assets/img/cover.png
        ---

        Intro with {{ site.baseurl }} and {{ "/about/" | relative_url }}.

        {% highlight csharp %}
        Console.WriteLine("hi");
        {% endhighlight %}
        """);
    File.WriteAllText(Path.Combine(path, "about.md"),
        """
        ---
        layout: page
        title: About
        permalink: /about/
        ---

        About this site.
        """);
    File.WriteAllText(Path.Combine(path, "assets", "img", "cover.png"), "png");
}

static void WriteHugoSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "content", "posts"));
    Directory.CreateDirectory(Path.Combine(path, "static", "img"));
    File.WriteAllText(Path.Combine(path, "hugo.toml"),
        """
        baseURL = 'https://hugo.example.com/site/'
        languageCode = 'zh-tw'
        title = 'Hugo Sample'
        """);
    File.WriteAllText(Path.Combine(path, "content", "posts", "hello.md"),
        """
        +++
        title = "Hugo Hello"
        date = "2026-03-01T09:00:00+08:00"
        draft = false
        categories = ["news"]
        tags = ["hugo"]
        +++

        {{< figure src="/img/sky.png" alt="Sky" caption="Blue sky" >}}

        Body from Hugo.
        """);
    File.WriteAllText(Path.Combine(path, "static", "img", "sky.png"), "png");
}

static void WriteHexoSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "source", "_posts"));
    File.WriteAllText(Path.Combine(path, "_config.yml"),
        """
        title: Hexo Sample
        url: https://hexo.example.com
        source_dir: source
        public_dir: public
        permalink: :year/:month/:day/:title/
        """);
    File.WriteAllText(Path.Combine(path, "package.json"),
        """
        { "name": "hexo-site", "hexo": {}, "dependencies": { "hexo": "^7.3.0" } }
        """);
    File.WriteAllText(Path.Combine(path, "source", "_posts", "2026-01-02-hexo-hello.md"),
        """
        ---
        title: Hexo Hello
        date: 2026-01-02 08:00:00
        categories:
          - news
          - hexo
        tags:
        - desktop
        ---

        {% asset_img foo.png alt text %}

        Hexo body.
        """);
}

static string? FirstMarkdown(params string[] directories)
{
    foreach (var directory in directories)
    {
        if (!Directory.Exists(directory))
            continue;
        var file = Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).FirstOrDefault();
        if (file is not null)
            return file;
    }

    return null;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
