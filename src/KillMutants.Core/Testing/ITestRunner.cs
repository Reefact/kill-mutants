using KillMutants.Projects;

namespace KillMutants.Testing;

/// <summary>Runs a project's tests and reports what happened.</summary>
/// <remarks>
/// A deliberately thin seam, not a plugin system. It exists for two concrete reasons: the
/// orchestrator can be tested without spawning processes, and it is the one place that would change
/// if KillMutants ever needed Microsoft Testing Platform's server mode. It is not an invitation to
/// support other test frameworks.
/// </remarks>
internal interface ITestRunner
{
    /// <summary>Lists the tests in a project without running them.</summary>
    Task<IReadOnlyList<TestName>> DiscoverAsync(
        TestProject testProject,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the tests described by <paramref name="request"/>.</summary>
    Task<TestRunOutcome> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default);
}
