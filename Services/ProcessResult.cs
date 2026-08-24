namespace Jekyller.Services;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool Success => ExitCode == 0;
    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StdErr))
                return StdOut.Trim();
            if (string.IsNullOrWhiteSpace(StdOut))
                return StdErr.Trim();
            return (StdOut.Trim() + Environment.NewLine + StdErr.Trim()).Trim();
        }
    }
}
