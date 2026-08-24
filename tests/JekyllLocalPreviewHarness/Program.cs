using Jekyller.Services;

Assert(JekyllLocalPreview.BuildSiteUrl(4000, "") == "http://127.0.0.1:4000/", "empty baseurl");
Assert(JekyllLocalPreview.BuildSiteUrl(4000, "/") == "http://127.0.0.1:4000/", "slash baseurl");
Assert(JekyllLocalPreview.BuildSiteUrl(4000, "blog") == "http://127.0.0.1:4000/blog/", "relative baseurl");
Assert(JekyllLocalPreview.BuildSiteUrl(4000, "/my-jekyll-site") == "http://127.0.0.1:4000/my-jekyll-site/", "absolute baseurl");

Assert(JekyllLocalPreview.ParseBaseUrlFromYaml("title: x\nbaseurl: \"/repo\"\n") == "/repo", "quoted yaml");
Assert(JekyllLocalPreview.ParseBaseUrlFromYaml("baseurl: \"\"\n") == "", "empty quoted yaml");

Assert(
    JekyllLocalPreview.EnsureSiteUrl("http://127.0.0.1:4000/", 4000, "/repo")
    == "http://127.0.0.1:4000/repo/",
    "inject missing baseurl");

Assert(JekyllLocalPreview.FallbackPostTitle("2026-08-23-post.md") == "post", "dated post slug");
Assert(JekyllLocalPreview.FallbackPostTitle("about.md") == "about", "page name");

var serveArgs = JekyllLocalPreview.BuildServeArguments(4000, liveReload: true, windows: true);
Assert(serveArgs.Contains("--future", StringComparison.Ordinal), serveArgs);
Assert(serveArgs.Contains("--drafts", StringComparison.Ordinal), serveArgs);
Assert(serveArgs.Contains("--force_polling", StringComparison.Ordinal), serveArgs);

Console.WriteLine("JEKYLL_LOCAL_PREVIEW_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
