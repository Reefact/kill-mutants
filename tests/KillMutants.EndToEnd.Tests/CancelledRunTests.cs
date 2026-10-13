using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A mutant's run stops at its first failure, and xUnit 4 cancels the rest of the suite to do it.
/// What the result file then says depends on when the cancellation lands, and the earliest it can
/// land leaves no test counted and one error: the cancellation itself, raised in assembly cleanup.
/// That is a failure, and has to be judged as one (RB-028).
/// </summary>
/// <remarks>
/// Measured on Reefact/slugger before the fix: mutants that broke 9, 25 and 55 of its unit tests
/// were reported as survived, reproducibly and with a single worker, because a run with no test and
/// an error read as a test project holding none of the mutant's tests. The session moved on to the
/// next project, whose tests passed. The more a mutant broke, the likelier its run was to be
/// cancelled early, so the survivors this invented were the mutants most obviously killed.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class CancelledRunTests
{
    [Fact]
    public async Task A_run_cancelled_before_it_counted_a_test_still_kills_the_mutant()
    {
        using var fixture = FixtureCopy.Create();

        var runner = new CancelsEveryFailureRunner(new XUnitTestRunner());

        MutationTestReport report = await new MutationTestSession(
                runner,
                "Release",
                timeoutPolicy: null,
                workerCount: 1,
                measureCoverage: true,
                exclude: null,
                catalog: MutatorCatalog.Of([MutatorName.Create("Comparison")]),
                verifyKills: 0)
            .RunAsync(fixture.Root, TestContext.Current.CancellationToken);

        Assert.True(runner.Cancelled > 0, "no mutant run failed, so the test proved nothing");

        // Every mutant in the fixture is killed by its tests; a run that only failed differently
        // must not change that.
        Assert.DoesNotContain(report.Results, result => result.Status == MutantStatus.Survived);
    }

    /// <summary>
    /// Runs the real suite and, wherever a mutant's run failed, reports what a run cancelled before
    /// any collection finished reports instead: nothing counted, one error.
    /// </summary>
    /// <remarks>
    /// The baseline and the coverage pass are left alone - they set no <c>StopOnFirstFailure</c>, and
    /// must stay honest or the run aborts before reaching a mutant.
    /// </remarks>
    private sealed class CancelsEveryFailureRunner(ITestRunner inner) : ITestRunner
    {
        private int _cancelled;

        public int Cancelled => Volatile.Read(ref _cancelled);

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            TestRunOutcome outcome = await inner.RunAsync(request, cancellationToken);

            if (!request.StopOnFirstFailure || !outcome.AnyFailed)
            {
                return outcome;
            }

            Interlocked.Increment(ref _cancelled);

            return new TestRunOutcome(
                Total: 0, Failed: 0, Errors: 1, outcome.Duration, TimedOut: false);
        }
    }
}
