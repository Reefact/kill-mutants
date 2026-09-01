using KillMutants.Testing;

namespace KillMutants.Core.Tests.Testing;

public class TestRunOutcomeTests
{
    private static TestRunOutcome Ran(int total, int failed, int errors = 0) =>
        new(total, failed, errors, TimeSpan.FromSeconds(1), TimedOut: false);

    [Fact]
    public void A_complete_run_with_nothing_failing_has_passed()
    {
        Assert.True(Ran(total: 2, failed: 0).AllPassed);
    }

    [Fact]
    public void A_run_with_a_failure_has_not_passed()
    {
        TestRunOutcome outcome = Ran(total: 2, failed: 1);

        Assert.True(outcome.AnyFailed);
        Assert.False(outcome.AllPassed);
    }

    [Fact]
    public void An_errored_test_counts_as_a_failure()
    {
        Assert.True(Ran(total: 2, failed: 0, errors: 1).AnyFailed);
    }

    [Fact]
    public void A_run_that_executed_nothing_is_not_a_pass()
    {
        // The xUnit console runner exits 0 when a filter matches no test. Treating that as success
        // would report a mutant as survived and invent a gap in the tests that does not exist.
        TestRunOutcome outcome = Ran(total: 0, failed: 0);

        Assert.True(outcome.NoTestsRan);
        Assert.False(outcome.AllPassed);
    }

    [Fact]
    public void A_timed_out_run_is_neither_a_pass_nor_an_empty_run()
    {
        TestRunOutcome outcome = TestRunOutcome.FromTimeout(TimeSpan.FromSeconds(5));

        Assert.True(outcome.TimedOut);
        Assert.False(outcome.AllPassed);
        Assert.False(outcome.NoTestsRan);
    }

    [Fact]
    public void A_crashed_run_is_neither_a_pass_nor_an_empty_run()
    {
        // A mutation can remove a recursion base case; the resulting StackOverflowException cannot
        // be caught and no result file is written. That is an outcome, not a broken tool.
        TestRunOutcome outcome = TestRunOutcome.FromCrash(TimeSpan.FromSeconds(2), "stack overflow");

        Assert.True(outcome.Crashed);
        Assert.Equal("stack overflow", outcome.CrashDetail);
        Assert.False(outcome.AllPassed);
        Assert.False(outcome.NoTestsRan);
    }

    /// <summary>
    /// Two places require a green suite - the baseline, and the instrumented build the coverage pass
    /// measures against - and both ask this one question, so it is the single place where "green"
    /// is defined. The case worth pinning is the empty run: the runner reports exit code zero for a
    /// filter that matched nothing, so silence here would let both checks pass on nothing at all.
    /// </summary>
    [Fact]
    public void A_clean_pass_has_no_reason_to_report()
    {
        Assert.Null(new TestRunOutcome(3, 0, 0, TimeSpan.Zero, TimedOut: false).WhyNotGreen());
    }

    [Fact]
    public void A_run_that_executed_nothing_says_so_rather_than_passing()
    {
        Assert.Equal(
            "it ran no tests at all",
            new TestRunOutcome(0, 0, 0, TimeSpan.Zero, TimedOut: false).WhyNotGreen());
    }

    [Fact]
    public void A_failure_is_reported_with_its_counts()
    {
        Assert.Equal(
            "2 of its 5 tests did not pass",
            new TestRunOutcome(5, 1, 1, TimeSpan.Zero, TimedOut: false).WhyNotGreen());
    }

    [Fact]
    public void A_timeout_and_a_crash_each_have_their_own_reason()
    {
        Assert.Equal("it exceeded its time budget", TestRunOutcome.FromTimeout(TimeSpan.Zero).WhyNotGreen());
        Assert.Contains(
            "could not be run",
            TestRunOutcome.FromCrash(TimeSpan.Zero, "exit 134")!.WhyNotGreen()!,
            StringComparison.Ordinal);
    }
}
