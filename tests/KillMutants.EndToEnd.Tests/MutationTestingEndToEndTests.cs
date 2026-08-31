using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Reporting;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The milestone 1 acceptance tests. They run the whole tool against real projects on disk: real
/// MSBuild, real Roslyn, a real emit, and a real xUnit 4 test application launched as a process.
/// </summary>
/// <remarks>
/// The pair matters more than either test alone. Proving a mutant is killed shows the loop runs;
/// proving the same mutant survives once the boundary case is removed shows the loop is actually
/// measuring the tests rather than reporting a kill for some unrelated reason.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class MutationTestingEndToEndTests
{
    [Fact]
    public async Task A_test_suite_that_checks_the_boundary_kills_the_mutant()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        // The founding acceptance criterion: `>=` becomes `>`, and the suite notices.
        MutantResult[] boundary = [.. report.Results
            .Where(result => result.Mutant.MutatedText == "age > 18")];

        Assert.NotEmpty(boundary);
        Assert.All(boundary, result =>
        {
            Assert.Equal(MutantStatus.Killed, result.Status);
            Assert.Equal("age >= 18", result.Mutant.OriginalText);
            Assert.Equal("Comparison", result.Mutant.Mutator.ToString());
            Assert.Equal("Ages.cs", Path.GetFileName(result.Mutant.Location.FilePath));
        });

        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal(0, report.Survived);
        Assert.Equal("100%", report.Score.ToString());
    }

    /// <summary>
    /// Proves the catalogue works against a real project rather than only against parsed snippets:
    /// every family produces at least one mutant, and the fixture's tests catch all of them.
    /// </summary>
    [Fact]
    public async Task Every_mutator_family_is_exercised_against_the_fixture()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        string[] families = [.. report.Results
            .Select(result => result.Mutant.Mutator.ToString())
            .Distinct()
            .Order(StringComparer.Ordinal)];

        Assert.Equal(
            ["Arithmetic", "Comparison", "LogicalOperator", "Negation", "StringLiteral"],
            families);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
    }

    [Fact]
    public async Task A_test_suite_that_skips_the_boundary_lets_the_mutant_survive()
    {
        using var fixture = FixtureCopy.Create();

        // Remove the boundary case. `age > 18` is still true for 42, so the weakened suite
        // cannot tell the mutant from the original - which is exactly the gap KillMutants exists
        // to report.
        string tests = await File.ReadAllTextAsync(fixture.TestSourceFile, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            fixture.TestSourceFile,
            tests.Replace("    [InlineData(18)]" + Environment.NewLine, string.Empty, StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        // Exactly one mutant escapes: IsAdult's boundary shift, which is indistinguishable from
        // the original once 18 itself is never passed to it. IsEligible's identical-looking
        // `age > 18` is still caught, because its own theory does test the boundary.
        MutantResult survivor = Assert.Single(
            report.Results, result => result.Status == MutantStatus.Survived);

        Assert.Equal("age >= 18", survivor.Mutant.OriginalText);
        Assert.Equal("age > 18", survivor.Mutant.MutatedText);
        Assert.Equal("Comparison", survivor.Mutant.Mutator.ToString());

        // Every other mutant in the fixture is still caught.
        Assert.Equal(report.Total - 1, report.Killed);
    }

    /// <summary>
    /// Closes RB-010. The deadline and the process kill were both in place, but nothing had ever
    /// watched them catch a mutant that genuinely never finishes - so Timeout was a status produced
    /// by code we had not seen work.
    /// </summary>
    [Fact]
    public async Task A_mutation_that_never_terminates_is_recorded_as_timed_out()
    {
        using var fixture = FixtureCopy.Create();
        fixture.UseCodeWhoseMutationNeverTerminates();

        // The default budget is deliberately generous; a short one keeps this test quick without
        // changing what is being demonstrated.
        var session = new MutationTestSession(
            new XUnitTestRunner(),
            "Release",
            new TimeoutPolicy(BaselineFactor: 2.0, Margin: TimeSpan.FromSeconds(5)));

        MutationTestReport report = await session.RunAsync(
            fixture.Root, TestContext.Current.CancellationToken);

        MutantResult timedOut = Assert.Single(
            report.Results, result => result.Status == MutantStatus.Timeout);

        Assert.Equal("value + 1", timedOut.Mutant.OriginalText);
        Assert.Equal("value - 1", timedOut.Mutant.MutatedText);
        Assert.Equal("Arithmetic", timedOut.Mutant.Mutator.ToString());

        // Every other mutant terminates and is caught, so the run still completes normally.
        Assert.Equal(report.Total - 1, report.Killed);
        Assert.Equal(0, report.Survived);

        // A mutation that hangs the suite did change observable behaviour: the tests noticed.
        Assert.Equal("100%", report.Score.ToString());
    }

    /// <summary>
    /// Regression test. A project setting UseMicrosoftTestingPlatformRunner generates the inverted
    /// entry point, where the test application defaults to the Microsoft Testing Platform host.
    /// Before `-automated` was passed on every run, our arguments reached that host, which rejected
    /// them with "Unknown option", exited 5 and wrote no result file - aborting the whole run. The
    /// tool must work on both project shapes.
    /// </summary>
    [Fact]
    public async Task A_project_defaulting_to_the_testing_platform_host_is_still_run_correctly()
    {
        using var fixture = FixtureCopy.Create();
        fixture.UseMicrosoftTestingPlatformRunner();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Results);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal("100%", report.Score.ToString());
    }

}

/// <summary>End-to-end runs build real projects, so they run one at a time.</summary>
[CollectionDefinition(nameof(SerialEndToEnd), DisableParallelization = true)]
public class SerialEndToEnd;
