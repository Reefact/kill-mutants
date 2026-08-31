namespace KillMutants.Testing;

/// <summary>What one run of a test suite produced.</summary>
/// <param name="Total">Tests that ran.</param>
/// <param name="Failed">Tests that failed an assertion.</param>
/// <param name="Errors">Tests that could not run to completion.</param>
/// <param name="Duration">How long the run took.</param>
/// <param name="TimedOut">True when the run exceeded its budget and was killed.</param>
public sealed record TestRunOutcome(int Total, int Failed, int Errors, TimeSpan Duration, bool TimedOut)
{
    /// <summary>True when the suite ran to completion with nothing failing.</summary>
    public bool AllPassed => !TimedOut && !AnyFailed && Total > 0;

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
    public bool NoTestsRan => !TimedOut && Total == 0;

    /// <summary>A run that was killed for exceeding its time budget.</summary>
    public static TestRunOutcome FromTimeout(TimeSpan duration) => new(0, 0, 0, duration, TimedOut: true);
}
