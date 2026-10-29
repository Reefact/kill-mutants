using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Selection;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A run that is stopped reports what it had judged, and only that (DEC0013). The stop is pulled
/// from inside the test runner, at an exact mutant, so these assertions do not depend on how fast
/// the machine is or on when a timer happens to fire.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class StoppedRunTests
{
    /// <summary>
    /// The third mutant's run is the one the stop lands on, and the host it ran in dies with it -
    /// which is what a Ctrl-C reaching the whole process group does. A crash counts as a kill, so
    /// keeping that verdict would credit the suite with a detection the stop caused.
    /// </summary>
    [Fact]
    public async Task A_stopped_run_keeps_the_verdicts_it_reached_and_drops_the_one_it_was_stopped_on()
    {
        using var fixture = FixtureCopy.Create();
        using var stop = new CancellationTokenSource();
        var heard = new Heard();

        var runner = new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 3);

        // One worker and no coverage, so the call sequence is exactly: the baseline, then one run per
        // mutant, in order.
        var session = new MutationTestSession(
            runner, "Release", timeoutPolicy: null, workerCount: 1, measureCoverage: false,
            verdicts: heard);

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken, stop.Token);

        Assert.False(report.IsComplete);
        Assert.Equal(21, report.Stopped!.Selected);
        Assert.Equal(2, report.Total);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal(["M1", "M2"], report.Results.Select(result => result.Mutant.Id.ToString()));

        // What was told as it landed is exactly what the report holds - not one more.
        Assert.Equal(
            report.Results.Select(result => result.Mutant.Key),
            heard.Verdicts.Select(result => result.Mutant.Key));
    }

    /// <summary>
    /// A stop that lands part way through re-verifying kills, once every mutant has its verdict. The
    /// population is whole, so the report is complete - score, gate and all - and what the stop cut
    /// short is the sample: the re-tests already done are counted, and no more.
    /// </summary>
    [Fact]
    public async Task A_stop_while_re_verifying_kills_leaves_a_fully_judged_run_complete()
    {
        using var fixture = FixtureCopy.Create();
        using var stop = new CancellationTokenSource();

        // Twenty-one mutant runs, then the re-verifications: the first re-test completes and the
        // second is the one the stop lands on.
        var runner = new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 23);

        var session = new MutationTestSession(
            runner, "Release", timeoutPolicy: null, workerCount: 1, measureCoverage: false,
            verifyKills: 3);

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken, stop.Token);

        Assert.True(report.IsComplete);
        Assert.Null(report.Stopped);
        Assert.Equal(21, report.Total);
        Assert.False(report.Score.IsUndefined);
        Assert.Equal(1, report.Environment!.KillsReVerified);
    }

    [Fact]
    public async Task A_run_stopped_before_it_starts_reports_that_it_selected_nothing()
    {
        using var fixture = FixtureCopy.Create();
        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        var session = new MutationTestSession(new XUnitTestRunner(), "Release");

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken, stop.Token);

        Assert.False(report.IsComplete);
        Assert.Null(report.Stopped!.Selected);
        Assert.Empty(report.Results);
        Assert.False(report.Scope.IsPartial);
    }

    /// <summary>
    /// Stopped before it read the change, a partial run is still a partial run. Recorded as the
    /// whole codebase, it would read as a full run that judged nothing - and the gate every partial
    /// run is would go with it.
    /// </summary>
    [Fact]
    public async Task A_partial_run_stopped_before_it_read_the_change_is_still_partial()
    {
        using var fixture = FixtureCopy.Create();
        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        var session = new MutationTestSession(
            new XUnitTestRunner(), "Release", changes: new NeverAsked());

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken, stop.Token);

        Assert.False(report.IsComplete);
        Assert.True(report.Scope.IsPartial);
        Assert.False(report.Scope.IsResolved);
    }

    /// <summary>
    /// Stopping and cancelling are different requests, and the difference is the whole point:
    /// a cancelled run is abandoned and reports nothing.
    /// </summary>
    [Fact]
    public async Task A_cancelled_run_is_abandoned_rather_than_reported()
    {
        using var fixture = FixtureCopy.Create();
        using var cancel = new CancellationTokenSource();

        await cancel.CancelAsync();

        var session = new MutationTestSession(new XUnitTestRunner(), "Release");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.RunAsync(fixture.Root, cancel.Token));
    }

    /// <summary>
    /// The other half of the contract: a run that is never stopped tells every verdict it reaches,
    /// once, and a timeout only after it has been confirmed alone.
    /// </summary>
    [Fact]
    public async Task A_complete_run_tells_every_verdict_once()
    {
        using var fixture = FixtureCopy.Create();
        var heard = new Heard();

        var session = new MutationTestSession(
            new XUnitTestRunner(), "Release", verdicts: heard);

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken);

        Assert.True(report.IsComplete);
        Assert.Equal(report.Total, heard.Verdicts.Count);
        Assert.Equal(
            report.Results.Select(result => result.Mutant.Key.ToString()).Order(StringComparer.Ordinal),
            heard.Verdicts.Select(result => result.Mutant.Key.ToString()).Order(StringComparer.Ordinal));
    }

    private sealed class Heard : IProgress<MutantResult>
    {
        private readonly Lock _gate = new();
        private readonly List<MutantResult> _verdicts = [];

        public IReadOnlyList<MutantResult> Verdicts
        {
            get
            {
                lock (_gate)
                {
                    return [.. _verdicts];
                }
            }
        }

        public void Report(MutantResult value)
        {
            lock (_gate)
            {
                _verdicts.Add(value);
            }
        }
    }

    /// <summary>
    /// Runs normally until the given mutant run, then asks for the stop and reports that run as the
    /// crash a host killed by the same signal would leave.
    /// </summary>
    private sealed class StopsOnMutantRunner(
        ITestRunner inner, CancellationTokenSource stop, int stopOnMutantRun) : ITestRunner
    {
        private int _mutantRuns;

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            // A mutant's run stops at its first failure; the baseline's does not.
            if (!request.StopOnFirstFailure ||
                Interlocked.Increment(ref _mutantRuns) != stopOnMutantRun)
            {
                return await inner.RunAsync(request, cancellationToken);
            }

            await stop.CancelAsync();

            return TestRunOutcome.FromCrash(TimeSpan.FromSeconds(1), "killed by SIGINT");
        }
    }

    /// <summary>A change source the run must never get as far as asking.</summary>
    private sealed class NeverAsked : IChangeSource
    {
        public string Root => throw new InvalidOperationException("The change was asked about.");

        public Task<ChangeSet> ChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The change was asked about.");

        public Task<ICodeSnapshot> OpenCodeBeforeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The change was asked about.");
    }
}
