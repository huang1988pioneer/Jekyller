using System.Text;
using System.Text.RegularExpressions;

namespace Jekyller.Services;

/// <summary>
/// Handles Jekyll YAML front matter. Unknown fields are preserved so editing
/// common metadata never destroys theme- or project-specific settings.
/// </summary>
public sealed partial class FrontMatterService
{
    public FrontMatterDocument Parse(string text)
    {
        text ??= string.Empty;
        var match = FrontMatterBlockRegex().Match(text);
        if (!match.Success)
            return new FrontMatterDocument { Body = text };

        var delimiter = match.Groups["delimiter"].Value;
        var fields = ParseFields(match.Groups["frontMatter"].Value, delimiter);
        var body = text[match.Length..].TrimStart('\r', '\n');
        while (FrontMatterBlockRegex().Match(body) is { Success: true } extra)
        {
            var extraFields = ParseFields(extra.Groups["frontMatter"].Value, extra.Groups["delimiter"].Value);
            foreach (var (key, value) in extraFields)
                fields.TryAdd(key, value);
            body = body[extra.Length..].TrimStart('\r', '\n');
        }

        return new FrontMatterDocument
        {
            Fields = fields,
            Body = body,
            Delimiter = delimiter
        };
    }

    public string Write(FrontMatterDocument document)
    {
        var fields = document.Fields;
        var orderedKeys = new[]
        {
            "layout", "title", "date", "published", "draft", "categories", "tags",
            "image", "images", "cover", "description", "permalink", "url",
            "icon", "order", "pin", "toc", "comments", "math", "mermaid", "slug"
        };
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var delimiter = document.Delimiter == "+++" ? "+++" : "---";
        var flavor = document.Flavor;
        var output = new StringBuilder(delimiter).Append('\n');

        foreach (var key in orderedKeys)
            AppendField(output, fields, key, emitted, delimiter, flavor);

        foreach (var key in fields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            AppendField(output, fields, key, emitted, delimiter, flavor);

        output.Append(delimiter).Append("\n\n");
        output.Append(document.Body.TrimStart('\r', '\n'));
        return output.ToString();
    }

    /// <summary>
    /// Replaces Markdown body while keeping the original front matter text intact.
    /// </summary>
    public string ReplaceBody(string markdown, string body)
    {
        markdown ??= string.Empty;
        var newBody = (body ?? string.Empty).TrimStart('\r', '\n');
        var match = FrontMatterBlockRegex().Match(markdown);
        if (!match.Success)
            return newBody;

        var header = markdown[..match.Length].TrimEnd('\r', '\n');
        return header + "\n\n" + newBody;
    }

    private static Dictionary<string, string> ParseFields(string frontMatter, string delimiter) =>
        delimiter == "+++"
            ? ParseTomlFields(frontMatter)
            : ParseYamlFields(frontMatter);

    private static Dictionary<string, string> ParseYamlFields(string frontMatter)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? nestedParent = null;
        foreach (var line in frontMatter.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            var trimmed = line.Trim();
            var indented = line.StartsWith(' ') || line.StartsWith('\t');
            if (nestedParent is not null && trimmed.StartsWith("- "))
            {
                AppendListItem(fields, nestedParent, trimmed[2..].Trim());
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (indented && nestedParent is not null)
            {
                fields[$"{nestedParent}.{key}"] = Unquote(value);
                continue;
            }

            nestedParent = string.IsNullOrEmpty(value) ? key : null;
            fields[key] = Unquote(value);
        }

        if (string.IsNullOrWhiteSpace(fields.GetValueOrDefault("image"))
            && fields.TryGetValue("image.path", out var imagePath)
            && !string.IsNullOrWhiteSpace(imagePath))
        {
            fields["image"] = imagePath;
        }

        return fields;
    }

    private static Dictionary<string, string> ParseTomlFields(string frontMatter)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontMatter.Split('\n'))
        {
            AddTomlFields(fields, line);
        }

        if (fields.Count == 0)
            AddTomlFields(fields, frontMatter);

        return fields;
    }

    private static void AddTomlFields(IDictionary<string, string> fields, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) return;
        foreach (Match match in TomlAssignmentRegex().Matches(line))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            fields[key] = Unquote(value);
        }
    }

    private static void AppendListItem(IDictionary<string, string> fields, string key, string rawItem)
    {
        foreach (var piece in SplitListItem(rawItem))
        {
            if (fields.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
                fields[key] = existing + ", " + piece;
            else
                fields[key] = piece;
        }
    }

    private static IEnumerable<string> SplitListItem(string item)
    {
        item = Unquote(item.Trim());
        if (string.IsNullOrWhiteSpace(item))
            return [];
        if (item.StartsWith('[') && item.EndsWith(']'))
            item = item[1..^1];
        return item.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Unquote)
            .Where(piece => piece.Length > 0);
    }

    private static void AppendField(
        StringBuilder output,
        IReadOnlyDictionary<string, string> fields,
        string key,
        ISet<string> emitted,
        string delimiter,
        FrontMatterFlavor flavor)
    {
        if (key.Contains('.', StringComparison.Ordinal))
            return;

        if (ShouldOmit(key, flavor))
            return;

        if (key.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            if (!emitted.Add(key)) return;
            AppendImageField(output, fields, flavor);
            return;
        }

        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || !emitted.Add(key))
            return;

        if (delimiter == "+++")
        {
            AppendTomlField(output, key, value);
            return;
        }

        if (key.Equals("draft", StringComparison.OrdinalIgnoreCase)
            || key.Equals("published", StringComparison.OrdinalIgnoreCase)
            || key.Equals("pin", StringComparison.OrdinalIgnoreCase)
            || key.Equals("toc", StringComparison.OrdinalIgnoreCase)
            || key.Equals("comments", StringComparison.OrdinalIgnoreCase)
            || key.Equals("math", StringComparison.OrdinalIgnoreCase)
            || key.Equals("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine($"{key}: {value.ToLowerInvariant()}");
            return;
        }

        if (key.Equals("categories", StringComparison.OrdinalIgnoreCase)
            || key.Equals("tags", StringComparison.OrdinalIgnoreCase)
            || key.Equals("images", StringComparison.OrdinalIgnoreCase)
            || key.Equals("photos", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Quote).ToArray();
            output.AppendLine($"{key}: [{string.Join(", ", values)}]");
            return;
        }

        if (key.Equals("date", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(value, out _))
        {
            output.AppendLine($"date: {value}");
            return;
        }

        output.AppendLine($"{key}: {Quote(value)}");
    }

    private static void AppendTomlField(StringBuilder output, string key, string value)
    {
        if (key.Equals("draft", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine($"draft = {value.ToLowerInvariant()}");
            return;
        }

        if (key.Equals("categories", StringComparison.OrdinalIgnoreCase)
            || key.Equals("tags", StringComparison.OrdinalIgnoreCase)
            || key.Equals("images", StringComparison.OrdinalIgnoreCase)
            || key.Equals("photos", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Quote).ToArray();
            output.AppendLine($"{key} = [{string.Join(", ", values)}]");
            return;
        }

        if (key.Equals("date", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(value, out _))
        {
            output.AppendLine($"date = {Quote(value)}");
            return;
        }

        output.AppendLine($"{key} = {Quote(value)}");
    }

    private static bool ShouldOmit(string key, FrontMatterFlavor flavor) => flavor switch
    {
        FrontMatterFlavor.Hugo =>
            key.Equals("layout", StringComparison.OrdinalIgnoreCase)
            || key.Equals("published", StringComparison.OrdinalIgnoreCase)
            || key.Equals("permalink", StringComparison.OrdinalIgnoreCase)
            || key.Equals("icon", StringComparison.OrdinalIgnoreCase)
            || key.Equals("order", StringComparison.OrdinalIgnoreCase),
        FrontMatterFlavor.Hexo =>
            key.Equals("layout", StringComparison.OrdinalIgnoreCase)
            || key.Equals("draft", StringComparison.OrdinalIgnoreCase)
            || key.Equals("url", StringComparison.OrdinalIgnoreCase)
            || key.Equals("icon", StringComparison.OrdinalIgnoreCase)
            || key.Equals("order", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static void AppendImageField(
        StringBuilder output,
        IReadOnlyDictionary<string, string> fields,
        FrontMatterFlavor flavor)
    {
        var path = fields.TryGetValue("image.path", out var nested) && !string.IsNullOrWhiteSpace(nested)
            ? nested
            : fields.TryGetValue("image", out var flat) ? flat : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (flavor != FrontMatterFlavor.Jekyll)
        {
            output.AppendLine($"image: {Quote(path.Trim())}");
            return;
        }

        output.AppendLine("image:");
        output.AppendLine($"  path: {Quote(path.Trim())}");
        if (fields.TryGetValue("image.alt", out var alt) && !string.IsNullOrWhiteSpace(alt))
            output.AppendLine($"  alt: {Quote(alt.Trim())}");
        if (fields.TryGetValue("image.width", out var width) && !string.IsNullOrWhiteSpace(width))
            output.AppendLine($"  width: {width.Trim()}");
        if (fields.TryGetValue("image.height", out var height) && !string.IsNullOrWhiteSpace(height))
            output.AppendLine($"  height: {height.Trim()}");
    }

    private static string Quote(string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']')) return value;
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            return string.Join(", ", value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Unquote));
        if (value.Length >= 2 && ((value[0] == '\"' && value[^1] == '\"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }

    [GeneratedRegex(@"\A(?<delimiter>---|\+\+\+)(?:\s*\r?\n(?<frontMatter>.*?)\r?\n\k<delimiter>|\s+(?<frontMatter>.*?)\s+\k<delimiter>)\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterBlockRegex();

    [GeneratedRegex(@"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?<value>'[^']*'|""[^""]*""|\[[^\]]*\]|[^\s]+)")]
    private static partial Regex TomlAssignmentRegex();
}

public enum FrontMatterFlavor
{
    Jekyll,
    Hugo,
    Hexo
}

public sealed class FrontMatterDocument
{
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = string.Empty;
    public string Delimiter { get; set; } = "---";
    public FrontMatterFlavor Flavor { get; set; } = FrontMatterFlavor.Jekyll;
}
