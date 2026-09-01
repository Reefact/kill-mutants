using KillMutants.Projects;

namespace KillMutants.Testing;

/// <summary>What to run, and under what conditions.</summary>
/// <param name="TestProject">The project to run.</param>
/// <param name="Timeout">How long the run may take before it is killed.</param>
/// <param name="StopOnFirstFailure">
/// Stop as soon as a test fails. A mutant is killed by its first failing test, so there is nothing
/// to learn from the rest of the suite.
/// </param>
/// <param name="TestNames">
/// The tests to run, or null for all of them. Selection is by name rather than by unique id, which
/// is what lets it survive being run from a sandbox copy (ADR-0006).
/// </param>
/// <param name="Environment">Extra environment variables for the test process.</param>
internal sealed record TestRunRequest(
    TestProject TestProject,
    TimeSpan Timeout,
    bool StopOnFirstFailure = false,
    IReadOnlyList<TestName>? TestNames = null,
    IReadOnlyDictionary<string, string>? Environment = null);
