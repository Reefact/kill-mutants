namespace KillMutants.Testing;

/// <summary>What one run of a test suite produced.</summary>
/// <param name="Total">Tests that ran.</param>
/// <param name="Failed">Tests that failed an assertion.</param>
/// <param name="Errors">Tests that could not run to completion.</param>
/// <param name="Duration">How long the run took.</param>
/// <param name="TimedOut">True when the run exceeded its budget and was killed.</param>
/// <param name="CrashDetail">Why the host died without reporting, or null when it reported normally.</param>
public sealed record TestRunOutcome(
    int Total,
    int Failed,
    int Errors,
    TimeSpan Duration,
    bool TimedOut,
    string? CrashDetail = null)
{
    /// <summary>
    /// True when the test host died without reporting a result at all.
    /// </summary>
    /// <remarks>
    /// A mutation can crash the process outright - a removed recursion base case gives a
    /// StackOverflowException, which .NET cannot catch - so no result file is written. That is a
    /// real outcome for a mutant, not a broken tool, and must not abort the whole run.
    /// </remarks>
    public bool Crashed => CrashDetail is not null;

    /// <summary>True when the suite ran to completion with nothing failing.</summary>
    public bool AllPassed => !TimedOut && !Crashed && !AnyFailed && Total > 0;

    /// <summary>True when at least one test failed or errored.</summary>
    public bool AnyFailed => Failed > 0 || Errors > 0;

    /// <summary>
    /// True when the run completed but executed no test at all.
    /// </summary>
    /// <remarks>
    /// This must never be mistaken for success. The xUnit console runner exits with code 0 when a
    /// filter matches nothing, so a tool trusting the exit code alone would record such a run as a
    /// surviving mutant - reporting a gap in the tests that does not exist.
    /// </remarks>
    public bool NoTestsRan => !TimedOut && !Crashed && Total == 0;

    /// <summary>A run that was killed for exceeding its time budget.</summary>
    public static TestRunOutcome FromTimeout(TimeSpan duration) => new(0, 0, 0, duration, TimedOut: true);

    /// <summary>A run whose host died before writing a result.</summary>
    public static TestRunOutcome FromCrash(TimeSpan duration, string detail) =>
        new(0, 0, 0, duration, TimedOut: false, CrashDetail: detail);
}
