using KillMutants.Execution;

namespace KillMutants.Core.Tests.Execution;

public class TimeoutPolicyTests
{
    [Fact]
    public void The_budget_scales_with_the_baseline_and_adds_the_margin()
    {
        var policy = new TimeoutPolicy(BaselineFactor: 3.0, Margin: TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(16), policy.For(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void An_instant_suite_still_gets_the_margin()
    {
        // Without the fixed allowance, a suite that runs in milliseconds would give mutants a budget
        // too small to cover process start, and healthy mutants would be reported as timing out.
        var policy = new TimeoutPolicy(BaselineFactor: 3.0, Margin: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), policy.For(TimeSpan.Zero));
    }

    [Fact]
    public void The_default_is_deliberately_generous()
    {
        // A mutant wrongly reported as timed out hides a real gap in the tests, which is worse than
        // waiting for it.
        Assert.Equal(TimeSpan.FromSeconds(33), TimeoutPolicy.Default.For(TimeSpan.FromSeconds(1)));
    }
}
