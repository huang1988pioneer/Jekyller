using System.Text.Json;
using Jekyller.Services;

var root = Path.Combine(Path.GetTempPath(), "JekyllerSettingsTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var settingsPath = Path.Combine(root, "settings.json");
    var first = Directory.CreateDirectory(Path.Combine(root, "first-site")).FullName;
    var second = Directory.CreateDirectory(Path.Combine(root, "second-site")).FullName;
    var third = Directory.CreateDirectory(Path.Combine(root, "third-site")).FullName;
    var missing = Path.Combine(root, "gone-site");

    var settings = new SettingsService(settingsPath);
    settings.Load();
    Assert(settings.IsAutoOpenLastProjectEnabled, "auto-open defaults to on");
    Assert(settings.GetAutoOpenProjectPath() is null, "no project before first open");
    Assert(settings.GetExistingRecentProjects().Count == 0, "recents start empty");

    settings.RememberOpenedProject(first);
    Assert(settings.Current.LastProjectPath == first, "last path is first site");
    Assert(settings.GetExistingRecentProjects().SequenceEqual([first]), "recents contains first");
    Assert(settings.GetAutoOpenProjectPath() == first, "auto-open returns first");

    settings.SetRepositoryUrl("GitHub", "https://github.com/a/site");
    settings.SetRepositoryUrl("GitLab", "https://gitlab.com/b/site");
    settings.SetSelectedGitPlatform("GitLab");
    Assert(settings.GetRepositoryUrl("GitHub") == "https://github.com/a/site", "GitHub URL is stored separately");
    Assert(settings.GetRepositoryUrl("GitLab") == "https://gitlab.com/b/site", "GitLab URL is stored separately");

    settings.RememberOpenedProject(second);
    settings.RememberOpenedProject(first);
    Assert(settings.Current.LastProjectPath == first, "reopening first makes it last");
    Assert(
        settings.GetExistingRecentProjects().SequenceEqual([first, second]),
        "reopen moves first to front without duplicating");

    var reloaded = new SettingsService(settingsPath);
    reloaded.Load();
    Assert(reloaded.Current.SelectedGitPlatform == "GitLab", "selected Git platform persists");
    Assert(reloaded.GetRepositoryUrl("GitHub") == "https://github.com/a/site", "per-platform URLs persist");
    Assert(reloaded.Current.LastProjectPath == first, "last path persists");
    Assert(
        reloaded.GetExistingRecentProjects().SequenceEqual([first, second]),
        "recents persist");

    reloaded.SetAutoOpenLastProject(false);
    Assert(reloaded.GetAutoOpenProjectPath() is null, "disabled auto-open returns null");
    reloaded.SetAutoOpenLastProject(true);

    reloaded.RememberOpenedProject(missing);
    Assert(reloaded.Current.LastProjectPath == missing, "missing last path is still remembered");
    Assert(
        reloaded.GetExistingRecentProjects().SequenceEqual([first, second]),
        "missing folders are skipped in the visible list");
    Assert(reloaded.GetAutoOpenProjectPath() == first, "auto-open falls back to next existing recent");

    reloaded.RemoveRecentProject(first);
    Assert(reloaded.Current.LastProjectPath == missing, "removing a fallback does not clear a newer last path");
    reloaded.RemoveRecentProject(missing);
    Assert(reloaded.Current.LastProjectPath == second, "removing last path falls back to remaining recent");
    Assert(reloaded.GetExistingRecentProjects().SequenceEqual([second]), "only second remains");

    var many = new SettingsService(Path.Combine(root, "many.json"));
    many.Load();
    var created = new List<string>();
    for (var i = 0; i < SettingsService.MaxRecentProjects + 3; i++)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, $"site-{i:00}")).FullName;
        created.Add(dir);
        many.RememberOpenedProject(dir);
    }

    var recents = many.GetExistingRecentProjects();
    Assert(recents.Count == SettingsService.MaxRecentProjects, "recents are capped");
    Assert(recents[0] == created[^1], "newest remembered project is first");
    Assert(!recents.Contains(created[0]), "oldest projects drop off the list");

    var legacyPath = Path.Combine(root, "legacy.json");
    File.WriteAllText(legacyPath,
        $$"""
        {
          "lastProjectPath": {{JsonSerializer.Serialize(third)}},
          "markdownEditorMode": "Wysiwyg"
        }
        """);
    var legacy = new SettingsService(legacyPath);
    legacy.Load();
    Assert(legacy.IsAutoOpenLastProjectEnabled, "legacy settings still auto-open");
    Assert(legacy.GetAutoOpenProjectPath() == third, "legacy last path is used");
    Assert(legacy.GetExistingRecentProjects().SequenceEqual([third]), "legacy last path becomes a recent");

    Console.WriteLine("SETTINGS_HARNESS_OK");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
