namespace KillMutants.Testing;

/// <summary>What one run of a test suite produced.</summary>
/// <param name="Total">Tests that ran.</param>
/// <param name="Failed">Tests that failed an assertion.</param>
/// <param name="Errors">
/// Failures the host reported outside a test's assertions: a test that could not run to completion,
/// or a fixture or cleanup that failed - which is where a cancelled run reports its cancellation.
/// </param>
/// <param name="Duration">How long the run took.</param>
/// <param name="TimedOut">True when the run exceeded its budget and was killed.</param>
/// <param name="CrashDetail">Why the host died without reporting, or null when it reported normally.</param>
/// <param name="FailedTests">
/// The tests that did not pass, named so they can be run again. Empty when none did, or when the
/// run produced no result file to read them from.
/// </param>
/// <param name="FailureMessages">
/// The first line of what each test in <paramref name="FailedTests"/> reported, for the ones that
/// reported anything.
/// </param>
public sealed record TestRunOutcome(
    int Total,
    int Failed,
    int Errors,
    TimeSpan Duration,
    bool TimedOut,
    string? CrashDetail = null,
    IReadOnlyList<TestName>? FailedTests = null,
    IReadOnlyDictionary<TestName, string>? FailureMessages = null)
{
    /// <summary>The tests that did not pass, named so they can be run again.</summary>
    /// <remarks>
    /// Method names rather than the runner's full case names, because a name that cannot be used to
    /// run the test again is not an answer. Measured against xUnit 4: passing
    /// <c>Class.Method(age: 18)</c> to <c>-method</c> matches nothing, while <c>Class.Method</c>
    /// matches the two cases behind it. A report naming killers nobody can re-run is what turns a
    /// disputed kill into an unanswerable question.
    /// </remarks>
    public IReadOnlyList<TestName> FailedTests { get; init; } = FailedTests ?? [];

    /// <summary>The first line of what each failed test reported, keyed by its name.</summary>
    /// <remarks>
    /// Kept for the baseline, which is the one place a failure is the user's to diagnose rather than
    /// the mutant's verdict. A count alone sent the reader of a red baseline on
    /// <c>Reefact/first-class-errors</c> looking for a bad rebuild, when the one line xUnit had
    /// written said <c>Could not locate FirstClassErrors.RequestBinder.csproj by walking up from
    /// '/tmp/…'</c> (#46).
    /// </remarks>
    public IReadOnlyDictionary<TestName, string> FailureMessages { get; init; } =
        FailureMessages ?? new Dictionary<TestName, string>();

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
    /// <para>
    /// This must never be mistaken for success. The xUnit console runner exits with code 0 when a
    /// filter matches nothing, so a tool trusting the exit code alone would record such a run as a
    /// surviving mutant - reporting a gap in the tests that does not exist.
    /// </para>
    /// <para>
    /// Nor for an empty run when the host reported an error. A mutant's run that sees one counts
    /// it as a failure; reading it as "no test here" instead moves on to the next test project and
    /// can end in a survivor. That is what a run cancelled by <c>-stopOnFail</c> looked like before
    /// its tests were counted - no test, one error - and the most destructive mutants were the ones
    /// it happened to (RB-028).
    /// </para>
    /// </remarks>
    public bool NoTestsRan => !TimedOut && !Crashed && Total == 0 && Errors == 0;

    /// <summary>
    /// Why this run is not a clean pass, phrased for a message, or null when it is one.
    /// </summary>
    /// <remarks>
    /// Both places that require a green suite - the baseline and the instrumented build the coverage
    /// pass measures against - need exactly these three checks in exactly this order, and each frames
    /// the answer differently. Deciding here means neither can forget one; in particular neither can
    /// forget <see cref="NoTestsRan"/>, which the runner reports as exit code zero.
    /// </remarks>
    public string? WhyNotGreen()
    {
        if (TimedOut)
        {
            return "it exceeded its time budget";
        }

        if (Crashed)
        {
            return $"the test application could not be run ({CrashDetail})";
        }

        if (NoTestsRan)
        {
            return "it ran no tests at all";
        }

        // An error with no test behind it is the host failing outside any test - a fixture, an
        // assembly cleanup - and "1 of its 0 tests" would describe a run that cannot exist.
        if (Total == 0)
        {
            return $"it reported {Errors} error(s) without running a test";
        }

        return AnyFailed
            ? $"{Failed + Errors} of its {Total} tests did not pass"
            : null;
    }

    /// <summary>A run that was killed for exceeding its time budget.</summary>
    public static TestRunOutcome FromTimeout(TimeSpan duration) => new(0, 0, 0, duration, TimedOut: true);

    /// <summary>A run whose host died before writing a result.</summary>
    public static TestRunOutcome FromCrash(TimeSpan duration, string detail) =>
        new(0, 0, 0, duration, TimedOut: false, CrashDetail: detail);
}
