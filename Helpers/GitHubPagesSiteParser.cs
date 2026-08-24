using System.Text.Json;
using System.Text.RegularExpressions;
using Jekyller.Models;

namespace Jekyller.Helpers;

public static partial class GitHubPagesSiteParser
{
    public static IReadOnlyList<GitHubPagesSiteItem> Parse(string? json, string? currentLogin = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
            trimmed = ConcatenatedJsonArraysRegex().Replace(trimmed, ",");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            var items = new List<GitHubPagesSiteItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var repo in EnumerateRepositories(document.RootElement))
            {
                var item = TryMap(repo, currentLogin);
                if (item is null || !seen.Add(item.FullName))
                    continue;
                items.Add(item);
            }

            return items;
        }
    }

    private static IEnumerable<JsonElement> EnumerateRepositories(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                yield return element;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var nested in element.EnumerateArray())
                {
                    if (nested.ValueKind == JsonValueKind.Object)
                        yield return nested;
                }
            }
        }
    }

    private static GitHubPagesSiteItem? TryMap(JsonElement repo, string? currentLogin)
    {
        var name = ReadString(repo, "name");
        var fullName = ReadString(repo, "full_name");
        var owner = string.Empty;
        if (repo.TryGetProperty("owner", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.Object)
            owner = ReadString(ownerElement, "login");

        if (string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(currentLogin))
            owner = currentLogin;

        if ((string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
            && !string.IsNullOrWhiteSpace(fullName))
        {
            var parts = fullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(owner))
                    owner = parts[0];
                if (string.IsNullOrWhiteSpace(name))
                    name = parts[1];
            }
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
            return null;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        var hasPages = repo.TryGetProperty("has_pages", out var pagesFlag)
                       && pagesFlag.ValueKind == JsonValueKind.True;
        var userSite = name.Equals($"{owner}.github.io", StringComparison.OrdinalIgnoreCase);
        if (!hasPages && !userSite)
            return null;

        var repositoryUrl = ReadString(repo, "html_url");
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            repositoryUrl = $"https://github.com/{owner}/{name}";

        var host = $"https://{owner.ToLowerInvariant()}.github.io";
        var pagesUrl = userSite ? $"{host}/" : $"{host}/{name}/";
        if (string.IsNullOrWhiteSpace(fullName))
            fullName = $"{owner}/{name}";

        return new GitHubPagesSiteItem
        {
            Owner = owner,
            Repository = name,
            FullName = fullName,
            RepositoryUrl = repositoryUrl,
            PagesUrl = pagesUrl,
            Description = ReadString(repo, "description"),
            IsPrivate = repo.TryGetProperty("private", out var privateFlag)
                        && privateFlag.ValueKind == JsonValueKind.True,
            IsUserOrOrganizationSite = userSite,
            HasPages = hasPages || userSite
        };
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : value.ToString().Trim();
    }

    [GeneratedRegex(@"\]\s*\[", RegexOptions.CultureInvariant)]
    private static partial Regex ConcatenatedJsonArraysRegex();
}
