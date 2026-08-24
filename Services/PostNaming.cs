using System.Text.RegularExpressions;

namespace Jekyller.Services;

public static partial class PostNaming
{
    public static string NextDefaultTitle(IEnumerable<string> existingNames, DateTime date)
    {
        var stamp = date.ToString("yyyyMMdd");
        var max = 0;
        foreach (var name in existingNames)
        {
            var number = ReadDefaultNumber(name, stamp);
            if (number > max)
                max = number;
        }

        return $"text{stamp}-{max + 1}";
    }

    public static bool IsDefaultTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        return DefaultTitleRegex().IsMatch(title.Trim());
    }

    private static int ReadDefaultNumber(string? name, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var stem = Path.GetFileNameWithoutExtension(name.Trim());
        var match = Regex.Match(
            stem,
            $@"(?:^|-)text{Regex.Escape(stamp)}-(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : 0;
    }

    [GeneratedRegex(@"^text\d{8}-\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefaultTitleRegex();
}
