using KillMutants.Execution;
using KillMutants.Projects;
using KillMutants.Testing;

namespace KillMutants.Core.Tests.Execution;

/// <summary>
/// Regression tests (#46). A red baseline used to report a count and one suggested cause, a bad
/// rebuild, when the cause on <c>Reefact/first-class-errors</c> was where the suite ran.
/// </summary>
public class RedBaselineMessageTests
{
    private static readonly TestProject InSandbox = new(
        "/repo/Sample.Tests/Sample.Tests.csproj",
        "/tmp/km/Sample.Tests-0/Sample.Tests.dll",
        "/tmp/km/Sample.Tests-0");

    [Fact]
    public void The_failed_tests_are_named_with_what_they_said()
    {
        TestName failed = TestName.Create("Sample.Tests.DogfoodingTests.Locates");
        var outcome = new TestRunOutcome(
            152, 1, 0, TimeSpan.Zero, TimedOut: false,
            FailedTests: [failed],
            FailureMessages: new Dictionary<TestName, string> { [failed] = "FileNotFoundException : Could not locate Sample.csproj" });

        string message = MutationTestSession.DescribeRedBaseline(InSandbox, "/repo/Sample.Tests/bin/Release/net10.0", outcome);

        Assert.Contains("1 of its 152 tests did not pass", message, StringComparison.Ordinal);
        Assert.Contains("Sample.Tests.DogfoodingTests.Locates", message, StringComparison.Ordinal);
        Assert.Contains("Could not locate Sample.csproj", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_causes_are_given_with_the_directories_that_tell_them_apart()
    {
        var outcome = new TestRunOutcome(10, 1, 0, TimeSpan.Zero, TimedOut: false);

        string message = MutationTestSession.DescribeRedBaseline(InSandbox, "/repo/Sample.Tests/bin/Release/net10.0", outcome);

        Assert.Contains("'/repo/Sample.Tests/bin/Release/net10.0'", message, StringComparison.Ordinal);
        Assert.Contains("'/tmp/km/Sample.Tests-0'", message, StringComparison.Ordinal);
        Assert.Contains("rebuilt the project", message, StringComparison.Ordinal);
        Assert.Contains("not the only ones", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_list_of_failures_is_cut_and_says_how_many_it_left_out()
    {
        TestName[] failed = [.. Enumerable.Range(1, 8).Select(i => TestName.Create($"Sample.Tests.T.M{i}"))];
        var outcome = new TestRunOutcome(8, 8, 0, TimeSpan.Zero, TimedOut: false, FailedTests: failed);

        string message = MutationTestSession.DescribeRedBaseline(InSandbox, "/repo/out", outcome);

        Assert.Contains("Sample.Tests.T.M5", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample.Tests.T.M6", message, StringComparison.Ordinal);
        Assert.Contains("and 3 more.", message, StringComparison.Ordinal);
    }
}
