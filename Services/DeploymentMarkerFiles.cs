namespace Jekyller.Services;

public static class DeploymentMarkerFiles
{
    public const string PrimaryFileName = "hugoer-deployment.json";
    public const string LegacyFileName = "jekyller-deployment.json";

    public static IReadOnlyList<string> ReadCandidates { get; } =
        [PrimaryFileName, LegacyFileName];

    public static IReadOnlyList<string> OutputFileNames => ReadCandidates;

    public static IEnumerable<string> ExpectedMarkerPaths(string sitePath)
    {
        foreach (var directory in new[] { sitePath, Path.Combine(sitePath, "static"), Path.Combine(sitePath, "public"), Path.Combine(sitePath, "_site") })
        foreach (var fileName in ReadCandidates)
            yield return Path.Combine(directory, fileName);
    }

    public static IEnumerable<string> SourceMarkerPaths(string sitePath)
    {
        foreach (var fileName in OutputFileNames)
        {
            yield return Path.Combine(sitePath, fileName);
            if (Directory.Exists(Path.Combine(sitePath, "static")))
                yield return Path.Combine(sitePath, "static", fileName);
        }
    }
}
