using Jekyller.Services;

var root = Path.Combine(Path.GetTempPath(), "JekyllerMediaAssetTests", Guid.NewGuid().ToString("N"));
var site = Path.Combine(root, "site");
Directory.CreateDirectory(site);

try
{
    Assert(MediaAssetService.Classify("a.PNG") == MediaKind.Image, "png is image");
    Assert(MediaAssetService.Classify("a.mp3") == MediaKind.Music, "mp3 is music");
    Assert(MediaAssetService.Classify("note.m4a") == MediaKind.Voice, "m4a is voice by default");
    Assert(MediaAssetService.Classify("clip.MP4") == MediaKind.Video, "mp4 is video");
    Assert(MediaAssetService.FolderName(MediaKind.Image) == "img", "image folder");
    Assert(MediaAssetService.FolderName(MediaKind.Music) == "audio", "audio folder");

    var photo = WriteTemp("photo.png", "png");
    var song = WriteTemp("song.mp3", "mp3");
    var clip = WriteTemp("clip.mp4", "mp4");
    var pdf = WriteTemp("paper.pdf", "pdf");
    var spaced = WriteTemp("evil name.png", "space");

    var imageAsset = MediaAssetService.Import(site, photo);
    Assert(imageAsset.Folder == "img", "copied into img");
    Assert(imageAsset.PublicUrl == "/assets/img/photo.png", $"public url {imageAsset.PublicUrl}");
    Assert(File.Exists(Path.Combine(site, "assets", "img", "photo.png")), "assets/img file exists");
    Assert(imageAsset.Markdown.Contains("![photo](/assets/img/photo.png)", StringComparison.Ordinal), imageAsset.Markdown);

    var duplicate = MediaAssetService.Import(site, photo);
    Assert(Path.GetFileName(duplicate.DestinationPath) == "photo-1.png", "collision gets unique name");
    Assert(duplicate.PublicUrl == "/assets/img/photo-1.png", duplicate.PublicUrl);

    var reused = MediaAssetService.Import(site, imageAsset.DestinationPath);
    Assert(reused.DestinationPath == imageAsset.DestinationPath, "importing an existing assets file reuses it");

    var musicAsset = MediaAssetService.Import(site, song);
    Assert(musicAsset.PublicUrl == "/assets/audio/song.mp3", musicAsset.PublicUrl);
    Assert(musicAsset.Markdown.Contains("<audio controls src=\"/assets/audio/song.mp3\"></audio>", StringComparison.Ordinal), musicAsset.Markdown);

    var videoAsset = MediaAssetService.Import(site, clip);
    Assert(videoAsset.Markdown.Contains("<video controls src=\"/assets/video/clip.mp4\"></video>", StringComparison.Ordinal), videoAsset.Markdown);

    var pdfAsset = MediaAssetService.Import(site, pdf);
    Assert(pdfAsset.PublicUrl == "/assets/pdf/paper.pdf", pdfAsset.PublicUrl);

    var spacedAsset = MediaAssetService.Import(site, spaced);
    Assert(spacedAsset.PublicUrl == "/assets/img/evil%20name.png", spacedAsset.PublicUrl);

    var html = """<p><img src="/assets/img/photo.png" alt="photo"/></p>""";
    var preview = MediaAssetService.ToPreviewHtml(html, site);
    Assert(preview.Contains("data:image/png;base64,", StringComparison.OrdinalIgnoreCase), preview);
    Assert(preview.Contains("data-jekyller-src", StringComparison.Ordinal), preview);
    Assert(preview.Contains("/assets/img/photo.png", StringComparison.Ordinal), preview);
    var roundTrip = MediaAssetService.FromPreviewHtml(preview, site);
    Assert(roundTrip.Contains("src=\"/assets/img/photo.png\"", StringComparison.Ordinal), roundTrip);

    Console.WriteLine("MEDIA_ASSET_HARNESS_OK");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
}

static string WriteTemp(string name, string content)
{
    var dir = Path.Combine(Path.GetTempPath(), "JekyllerMediaSrc", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, name);
    File.WriteAllText(path, content);
    return path;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
