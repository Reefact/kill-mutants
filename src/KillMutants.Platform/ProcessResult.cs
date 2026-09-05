namespace KillMutants.Processes;

/// <summary>What running an external process produced.</summary>
/// <param name="ExitCode">The process exit code, or -1 when it was killed for exceeding its budget.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
/// <param name="Duration">How long the process ran.</param>
/// <param name="TimedOut">True when the process was killed for exceeding its time budget.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    /// <summary>True when the process finished on its own with a zero exit code.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>Both output streams, for diagnostics.</summary>
    public string CombinedOutput =>
        string.IsNullOrEmpty(StandardError) ? StandardOutput : StandardOutput + Environment.NewLine + StandardError;
}
