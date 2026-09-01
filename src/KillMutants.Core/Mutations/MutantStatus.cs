namespace KillMutants.Mutations;

/// <summary>What became of one mutant.</summary>
/// <remarks>
/// What each of these is <em>worth</em> is not decided here but in <see cref="MutantOutcome"/>, so
/// that no reporter or threshold has to make that judgement for itself.
/// </remarks>
public enum MutantStatus
{
    /// <summary>At least one test failed: the test suite noticed the change.</summary>
    Killed,

    /// <summary>Every test passed: the change went unnoticed.</summary>
    Survived,

    /// <summary>The mutated code did not compile, so the mutant could not be tested.</summary>
    CompileError,

    /// <summary>The test run exceeded its time budget, most likely a mutation-induced endless loop.</summary>
    Timeout,

    /// <summary>No test exercises the mutated code, so running the suite would prove nothing.</summary>
    NoCoverage,
}
