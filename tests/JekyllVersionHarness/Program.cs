using Jekyller.Services;

var update = JekyllVersionComparison.Create("jekyll 4.3.4", "jekyll (4.4.1)", true);
Assert(update.IsInstalled, "installed Jekyll should be detected");
Assert(update.CheckSucceeded, "successful RubyGems output should be recognized");
Assert(update.IsUpdateAvailable, "older installed Jekyll should report an update");
Assert(update.InstalledVersion == "4.3.4" && update.LatestVersion == "4.4.1",
    "installed and latest versions should be preserved for the user message");

var latest = JekyllVersionComparison.Create("jekyll 4.4.1", "jekyll (4.4.1)", true);
Assert(!latest.IsUpdateAvailable, "matching versions should report latest");
Assert(latest.Message.Contains("已是最新版本", StringComparison.Ordinal),
    "latest status should be explicit to the user");

var apiResult = JekyllVersionComparison.Create(
    "jekyll 4.3.4",
    "{\"name\":\"jekyll\",\"version\":\"4.4.1\"}",
    true);
Assert(apiResult.IsUpdateAvailable && apiResult.LatestVersion == "4.4.1",
    "RubyGems API JSON should provide the latest stable version without writing gem cache files");

var offline = JekyllVersionComparison.Create("jekyll 4.4.1", string.Empty, false);
Assert(offline.IsInstalled && !offline.CheckSucceeded,
    "offline checks should preserve the installed version without claiming success");
Assert(offline.Message.Contains("暫時無法", StringComparison.Ordinal),
    "offline status should explain that the check can be retried");

var missing = JekyllVersionComparison.Create(string.Empty, string.Empty, false);
Assert(!missing.IsInstalled && missing.Message.Contains("尚未安裝", StringComparison.Ordinal),
    "missing Jekyll should direct the user to install it");

Console.WriteLine("JEKYLL_VERSION_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
