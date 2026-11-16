using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A timeout counts as a detection, so a mutant wrongly reported as timed out <em>raises</em> the
/// score: the suite is credited with catching something it never noticed. The budget comes from a
/// baseline measured alone and is then spent by workers competing for the machine, so a healthy but
/// slow mutant can exceed it for no reason of its own.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class TimeoutConfirmationTests
{
    /// <summary>
    /// Injects one timeout into the first mutant's run, exactly as contention would - while another
    /// worker is testing a mutant beside it - and requires the run to reach the mutant's real verdict
    /// anyway. Before every timeout was confirmed on an idle machine, this reported one Timeout - a
    /// detection nothing had detected.
    /// </summary>
    /// <remarks>
    /// Two workers, and the injected timeout held until the other one has started: a timeout reached
    /// with nothing beside it is believed without a re-run (#83), so the contention this test stands
    /// for has to be there for the confirmation to be asked for at all.
    /// </remarks>
    [Fact]
    public async Task A_timeout_that_does_not_reproduce_alone_is_not_believed()
    {
        using var fixture = FixtureCopy.Create();

        var runner = new TimesOutOnceRunner(new XUnitTestRunner());

        // Coverage off so the call sequence is exactly: the baseline, then one run per mutant.
        var session = new MutationTestSession(
            runner, "Release", timeoutPolicy: null, workerCount: 2, measureCoverage: false);

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken);

        Assert.True(runner.Injected, "the test never injected a timeout, so it proved nothing");
        Assert.Equal(0, report.TimedOut);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal("100%", report.Score.ToString());
    }

    /// <summary>
    /// The other half: confirming timeouts must not turn a genuine endless loop into a survivor.
    /// </summary>
    [Fact]
    public async Task A_timeout_that_does_reproduce_is_still_recorded()
    {
        using var fixture = FixtureCopy.Create();
        fixture.UseCodeWhoseMutationNeverTerminates();

        var session = new MutationTestSession(
            new XUnitTestRunner(),
            "Release",
            new TimeoutPolicy(BaselineFactor: 2.0, Margin: TimeSpan.FromSeconds(5)));

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken);

        MutantResult timedOut = Assert.Single(
            report.Results, result => result.Status == MutantStatus.Timeout);

        Assert.Equal("value - 1", timedOut.Mutant.MutatedText);

        // Its duration is that of the run that ran the whole budget alone - the confirmation, or the
        // first pass when nothing was running beside it (#83) - at least the five second margin, and
        // a first pass spent under load is not added to it (DEC0031, #75).
        Assert.NotNull(timedOut.Duration);
        Assert.True(timedOut.Duration >= TimeSpan.FromSeconds(5), $"{timedOut.Duration} is shorter than the budget's margin");
        Assert.True(timedOut.Duration < report.Duration / 2 + TimeSpan.FromSeconds(1), "the first pass was added to the confirmation");
    }

    /// <summary>
    /// #83: a timeout reached with no other mutant running beside it is already what the
    /// confirmation would measure, so it is believed as it is rather than paid for a second time.
    /// With one worker, every mutant runs alone, and a genuine endless loop costs one budget, not two.
    /// </summary>
    [Fact]
    public async Task A_timeout_reached_alone_is_not_run_again()
    {
        using var fixture = FixtureCopy.Create();
        fixture.UseCodeWhoseMutationNeverTerminates();

        var runner = new CountingRunner(new XUnitTestRunner());

        // Coverage off so the call sequence is exactly: the baseline, then one run per mutant.
        var session = new MutationTestSession(
            runner,
            "Release",
            new TimeoutPolicy(BaselineFactor: 2.0, Margin: TimeSpan.FromSeconds(5)),
            workerCount: 1,
            measureCoverage: false);

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken);

        MutantResult timedOut = Assert.Single(
            report.Results, result => result.Status == MutantStatus.Timeout);

        Assert.Equal("value - 1", timedOut.Mutant.MutatedText);
        Assert.Null(timedOut.Overturned);
        Assert.Equal(1 + report.Results.Count, runner.Runs);
    }

    /// <summary>
    /// Reports a timeout for the first mutant it is asked to run, once another mutant has started
    /// beside it, then behaves normally.
    /// </summary>
    private sealed class TimesOutOnceRunner(ITestRunner inner) : ITestRunner
    {
        private readonly TaskCompletionSource _neighbourStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _runs;

        public bool Injected { get; private set; }

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            // Run 1 is the baseline, which must stay honest or the whole run aborts. Run 2 is the
            // first mutant, and run 3 the one the other worker starts beside it.
            int run = Interlocked.Increment(ref _runs);

            if (run == 3)
            {
                _neighbourStarted.TrySetResult();
            }

            if (run != 2)
            {
                return await inner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }

            await _neighbourStarted.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            Injected = true;

            return TestRunOutcome.FromTimeout(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Counts the test runs it is asked for, and changes nothing about them.</summary>
    private sealed class CountingRunner(ITestRunner inner) : ITestRunner
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _runs);

            return inner.RunAsync(request, cancellationToken);
        }
    }
}
