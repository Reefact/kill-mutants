namespace KillMutants.Mutations;

/// <summary>The outcome of testing one mutant.</summary>
/// <remarks>
/// Milestone 1 only ever produces <see cref="Killed"/> and <see cref="Survived"/>. The rest of the
/// vocabulary is fixed now so that later milestones add behaviour rather than reshape the model.
/// </remarks>
public enum MutantStatus
{
    /// <summary>Not tested yet.</summary>
    Pending,

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
