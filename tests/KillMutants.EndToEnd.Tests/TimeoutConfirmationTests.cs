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
    /// Injects one timeout into the first mutant's run, exactly as contention would, and requires the
    /// run to reach the mutant's real verdict anyway. Before every timeout was confirmed on an idle
    /// machine, this reported one Timeout - a detection nothing had detected.
    /// </summary>
    [Fact]
    public async Task A_timeout_that_does_not_reproduce_alone_is_not_believed()
    {
        using var fixture = FixtureCopy.Create();

        var runner = new TimesOutOnceRunner(new XUnitTestRunner());

        // Coverage off so the call sequence is exactly: the baseline, then one run per mutant.
        var session = new MutationTestSession(
            runner, "Release", timeoutPolicy: null, workerCount: 1, measureCoverage: false);

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
    }

    /// <summary>Reports a timeout for the first mutant it is asked to run, then behaves normally.</summary>
    private sealed class TimesOutOnceRunner(ITestRunner inner) : ITestRunner
    {
        private int _runs;

        public bool Injected { get; private set; }

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            // Run 1 is the baseline, which must stay honest or the whole run aborts. Run 2 is the
            // first mutant.
            if (Interlocked.Increment(ref _runs) != 2)
            {
                return inner.RunAsync(request, cancellationToken);
            }

            Injected = true;

            return Task.FromResult(TestRunOutcome.FromTimeout(TimeSpan.FromSeconds(1)));
        }
    }
}
