using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// Two reports without their environment are not comparable, and nobody notices. These are the
/// facts that decide whether a verdict is about the code or about the machine it ran on.
/// </summary>
public class RunEnvironmentTests
{
    /// <summary>
    /// A run that verified none of its kills and a run that verified all of them both report "no
    /// disagreements". Only one of those means anything, so the sample size is stated.
    /// </summary>
    [Fact]
    public void A_run_that_re_verified_nothing_says_so()
    {
        Assert.Contains("no kills re-verified", Describe(killsReVerified: 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_re_verified_some_says_how_many()
    {
        Assert.Contains(
            "10 kill(s) re-verified alone", Describe(killsReVerified: 10), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the budget reads as the formula it is: a limit a reader cannot recompute is one they have
    /// to take on trust, which is what made a timeout unexplainable on another tool.
    /// </summary>
    [Fact]
    public void The_budget_is_published_with_everything_it_rests_on()
    {
        string line = Describe(killsReVerified: 0);

        Assert.Contains("46.2 s (5.4 s baseline alone", line, StringComparison.Ordinal);
        Assert.Contains("× 3 + 30 s", line, StringComparison.Ordinal);
    }

    private static string Describe(int killsReVerified) =>
        new RunEnvironment(
                ".NET 10.0.0",
                "Ubuntu",
                ProcessorCount: 4,
                WorkerCount: 2,
                TestFramework: "xUnit 4.0.0",
                TimeoutBudgets: [new TimeBudget(TimeSpan.FromSeconds(5.4), 3.0, TimeSpan.FromSeconds(30))],
                KillsReVerified: killsReVerified)
            .ToString();
}
