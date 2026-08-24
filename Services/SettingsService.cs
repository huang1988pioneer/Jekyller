using System.Text.Json;
using Jekyller.Helpers;
using Jekyller.Models;

namespace Jekyller.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    bool IsAutoOpenLastProjectEnabled { get; }
    void Load();
    void Save();
    void RememberOpenedProject(string path);
    void RemoveRecentProject(string path);
    void SetAutoOpenLastProject(bool enabled);
    void SetMarkdownEditorMode(string mode);
    string? GetAutoOpenProjectPath();
    IReadOnlyList<string> GetExistingRecentProjects();
}

public sealed class SettingsService : ISettingsService
{
    public const int MaxRecentProjects = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public SettingsService() : this(PathHelper.SettingsPath)
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Current => _settings;

    public bool IsAutoOpenLastProjectEnabled => _settings.AutoOpenLastProject != false;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(_settingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize();
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void RememberOpenedProject(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
            return;

        _settings.LastProjectPath = normalized;
        InsertRecent(normalized);
        Save();
    }

    public void RemoveRecentProject(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
            return;

        _settings.RecentProjects.RemoveAll(p =>
            string.Equals(NormalizePath(p), normalized, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(_settings.LastProjectPath, normalized, StringComparison.OrdinalIgnoreCase))
            _settings.LastProjectPath = _settings.RecentProjects.FirstOrDefault();

        Save();
    }

    public void SetAutoOpenLastProject(bool enabled)
    {
        if (_settings.AutoOpenLastProject == enabled)
            return;

        _settings.AutoOpenLastProject = enabled;
        Save();
    }

    public void SetMarkdownEditorMode(string mode)
    {
        _settings.MarkdownEditorMode = mode;
        Save();
    }

    public string? GetAutoOpenProjectPath()
    {
        if (!IsAutoOpenLastProjectEnabled)
            return null;

        return GetExistingRecentProjects().FirstOrDefault();
    }

    public IReadOnlyList<string> GetExistingRecentProjects()
    {
        Normalize();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = new List<string>();

        foreach (var candidate in EnumerateRecentCandidates())
        {
            if (!seen.Add(candidate))
                continue;
            if (!Directory.Exists(candidate))
                continue;
            existing.Add(candidate);
        }

        return existing;
    }

    private IEnumerable<string> EnumerateRecentCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastProjectPath))
            yield return _settings.LastProjectPath;

        foreach (var path in _settings.RecentProjects)
        {
            var normalized = NormalizePath(path);
            if (normalized is not null)
                yield return normalized;
        }
    }

    private void InsertRecent(string path)
    {
        _settings.RecentProjects.RemoveAll(p =>
            string.Equals(NormalizePath(p), path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentProjects.Insert(0, path);
        TrimRecents();
    }

    private void Normalize()
    {
        _settings.RecentProjects ??= [];

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var last = NormalizePath(_settings.LastProjectPath);
        _settings.LastProjectPath = last;
        if (last is not null && seen.Add(last))
            normalized.Add(last);

        foreach (var path in _settings.RecentProjects)
        {
            var item = NormalizePath(path);
            if (item is null || !seen.Add(item))
                continue;
            normalized.Add(item);
        }

        _settings.RecentProjects = normalized;
        TrimRecents();
    }

    private void TrimRecents()
    {
        if (_settings.RecentProjects.Count <= MaxRecentProjects)
            return;

        _settings.RecentProjects.RemoveRange(
            MaxRecentProjects,
            _settings.RecentProjects.Count - MaxRecentProjects);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }
}
