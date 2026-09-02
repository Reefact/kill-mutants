using KillMutants.Cli;
using KillMutants.Execution;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Two values that got past every gate and disarmed the thing they were supposed to configure.
/// </summary>
public class RefusedSettingsTests
{
    /// <summary>
    /// The command line refused a worker count below one; the library API did not. A caller that
    /// reached the session directly got a run with no workers, which builds no sandbox and then
    /// indexes the first one.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_session_refuses_a_worker_count_below_one(int workers)
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => new MutationTestSession(new XUnitTestRunner(), "Release", workerCount: workers));

        Assert.Equal("workers", refusal.ParamName);
    }

    [Fact]
    public void A_session_refuses_a_negative_sample_of_kills_to_verify()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MutationTestSession(new XUnitTestRunner(), "Release", verifyKills: -1));
    }

    /// <summary>And the values it should take are taken, or the guard is just an obstacle.</summary>
    [Fact]
    public void The_smallest_values_a_session_allows_are_accepted()
    {
        _ = new MutationTestSession(
            new XUnitTestRunner(), "Release", workerCount: 1, verifyKills: 0);
    }

    /// <summary>
    /// NaN parses, and every comparison with it is false - so it passes the range check here and the
    /// one the verdict makes at the end. A quality gate that lets NaN through stops being a gate
    /// without saying anything.
    /// </summary>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void A_threshold_that_is_not_a_number_is_refused_on_the_command_line(string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["/work", "--break-at", value]));

        Assert.Contains("percentage between 0 and 100", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_threshold_that_is_not_a_number_is_refused_in_the_file()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(
                CommandLineOptions.Parse(["/work"]), new ConfigurationFile(BreakAt: double.NaN)));

        Assert.Contains("percentage between 0 and 100", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>An ordinary threshold still passes, on both routes.</summary>
    [Fact]
    public void An_ordinary_threshold_is_accepted()
    {
        Assert.Equal(70, CommandLineOptions.Parse(["/work", "--break-at", "70"]).Threshold);
        Assert.Equal(
            70,
            RunSettings.From(
                CommandLineOptions.Parse(["/work"]), new ConfigurationFile(BreakAt: 70)).Threshold);
    }
}
