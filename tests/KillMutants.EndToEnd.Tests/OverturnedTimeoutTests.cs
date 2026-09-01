using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The budget is measured with nothing else running and then spent by workers competing for the
/// machine, so a healthy mutant can exceed it for that reason alone. Every timeout is therefore
/// re-run on its own before it is believed - and a timeout that does not survive that re-run has to
/// leave a trace, or the run quietly gets better than it was measured.
/// </summary>
/// <remarks>
/// A field report on another tool is what this guards against: 2 505 of 4 575 mutants counted as
/// detected purely because they timed out under a concurrency the budget had not been calibrated
/// for, and a headline of 100%. The correction alone is not enough. Without a record of how often it
/// fired, nobody can tell a run whose budget is sound from one whose budget is rescued on every
/// other mutant.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class OverturnedTimeoutTests
{
    [Fact]
    public async Task A_timeout_that_does_not_survive_being_re_run_alone_is_recorded_as_corrected()
    {
        using var fixture = FixtureCopy.Create();

        // Gives the run a mutant that genuinely survives, so there is a verdict to correct to.
        fixture.AddPartlyTestedCode();

        var runner = new TimesOutOnceRunner(new XUnitTestRunner());

        MutationTestReport report = await Run(fixture.Root, runner);

        Assert.True(runner.Injected, "the test never injected a timeout, so it proved nothing");

        MutantResult corrected = Assert.Single(
            report.Results, result => result.Overturned is not null);

        // The corrected verdict is the one kept: the re-run alone is the better measurement, and
        // unlike a disputed kill there is no question which of the two to believe.
        Assert.NotEqual(MutantStatus.Timeout, corrected.Status);
        Assert.Null(corrected.Disagreement);
        Assert.Contains("re-run on its own", corrected.Overturned!, StringComparison.Ordinal);

        // And it is said where a reader will see it, with the key needed to settle it by hand.
        RunWarning warning = Assert.Single(
            report.Warnings, w => w.Text.Contains("timed out while the workers", StringComparison.Ordinal));

        Assert.Contains(corrected.Mutant.Key.ToString(), warning.Text, StringComparison.Ordinal);

        // The corrected mutant is no longer counted as detected, which is the whole point.
        Assert.Equal(0, report.TimedOut);
    }

    /// <summary>
    /// And a run whose timeouts are real - or which has none - says nothing, or the check would cry
    /// wolf on every project.
    /// </summary>
    [Fact]
    public async Task A_run_with_nothing_to_correct_says_nothing()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await Run(fixture.Root, new XUnitTestRunner());

        Assert.All(report.Results, result => Assert.Null(result.Overturned));
        Assert.DoesNotContain(
            report.Warnings,
            w => w.Text.Contains("timed out while the workers", StringComparison.Ordinal));
    }

    private static Task<MutationTestReport> Run(string root, ITestRunner runner) =>
        new MutationTestSession(
                runner,
                "Release",
                timeoutPolicy: null,
                workerCount: 1,
                measureCoverage: true,
                exclude: null,
                catalog: MutatorCatalog.Of([MutatorName.Create("Comparison")]),
                verifyKills: 0)
            .RunAsync(root, TestContext.Current.CancellationToken);

    /// <summary>Reports one spurious timeout, then behaves.</summary>
    /// <remarks>
    /// Placed on a mutant run rather than on the baseline or the coverage pass, both of which must
    /// stay honest or the run aborts before reaching the part under test. <c>StopOnFirstFailure</c>
    /// is what tells them apart: only a mutant run sets it.
    /// </remarks>
    private sealed class TimesOutOnceRunner(ITestRunner inner) : ITestRunner
    {
        public bool Injected { get; private set; }

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            if (!Injected && request.StopOnFirstFailure)
            {
                Injected = true;

                return TestRunOutcome.FromTimeout(request.Timeout);
            }

            return await inner.RunAsync(request, cancellationToken);
        }
    }
}
