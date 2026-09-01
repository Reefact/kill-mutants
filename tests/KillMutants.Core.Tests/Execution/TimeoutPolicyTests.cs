using KillMutants.Execution;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Execution;

public class TimeoutPolicyTests
{
    [Fact]
    public void The_budget_scales_with_the_baseline_and_adds_the_margin()
    {
        var policy = new TimeoutPolicy(BaselineFactor: 3.0, Margin: TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(16), policy.For(TimeSpan.FromSeconds(2)).Budget);
    }

    [Fact]
    public void An_instant_suite_still_gets_the_margin()
    {
        // Without the fixed allowance, a suite that runs in milliseconds would give mutants a budget
        // too small to cover process start, and healthy mutants would be reported as timing out.
        var policy = new TimeoutPolicy(BaselineFactor: 3.0, Margin: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), policy.For(TimeSpan.Zero).Budget);
    }

    [Fact]
    public void The_default_is_deliberately_generous()
    {
        // A mutant wrongly reported as timed out hides a real gap in the tests, which is worse than
        // waiting for it.
        Assert.Equal(TimeSpan.FromSeconds(33), TimeoutPolicy.Default.For(TimeSpan.FromSeconds(1)).Budget);
    }

    /// <summary>
    /// The budget carries the calculation, not just its result. A number a reader cannot recompute
    /// is one they have to take on trust, and a timeout is unexplainable without it - which is what
    /// made a field report on another tool cost a day: it never wrote its budget down at all.
    /// </summary>
    [Fact]
    public void The_budget_says_what_it_was_derived_from()
    {
        TimeBudget budget = TimeoutPolicy.Default.For(TimeSpan.FromSeconds(5.4));

        Assert.Equal(TimeSpan.FromSeconds(5.4), budget.Baseline);
        Assert.Equal(3.0, budget.Factor);
        Assert.Equal(TimeSpan.FromSeconds(30), budget.Margin);

        // And reads as the formula it is, so the arithmetic can be checked by eye.
        Assert.Equal("46.2 s (5.4 s baseline alone \u00d7 3 + 30 s)", budget.ToString());
    }
}
