using KillMutants.Projects;

namespace KillMutants.Testing;

/// <summary>Runs a project's tests and reports what happened.</summary>
/// <remarks>
/// A deliberately thin seam, not a plugin system. It exists for two concrete reasons: the
/// orchestrator can be tested without spawning processes, and if milestone 5 shows that
/// test-to-mutant mapping needs Microsoft Testing Platform's server mode, this is the one place
/// that would change. It is not an invitation to support other test frameworks.
/// </remarks>
internal interface ITestRunner
{
    /// <summary>Runs the tests, giving up after <paramref name="timeout"/>.</summary>
    /// <param name="testProject">The project to run.</param>
    /// <param name="timeout">How long the run may take before it is killed.</param>
    /// <param name="stopOnFirstFailure">
    /// When true, stop as soon as a test fails. A mutant is killed by its first failing test, so
    /// there is nothing to learn from the rest of the suite.
    /// </param>
    /// <param name="cancellationToken">Cancels the run.</param>
    Task<TestRunOutcome> RunAsync(
        TestProject testProject,
        TimeSpan timeout,
        bool stopOnFirstFailure,
        CancellationToken cancellationToken = default);
}
