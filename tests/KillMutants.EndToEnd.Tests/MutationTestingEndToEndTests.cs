using KillMutants.Mutations;
using KillMutants.Reporting;

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
        MutantResult boundary = Assert.Single(
            report.Results, result => result.Mutant.MutatedText == "age > 18");

        Assert.Equal(MutantStatus.Killed, boundary.Status);
        Assert.Equal("age >= 18", boundary.Mutant.OriginalText);
        Assert.Equal("Comparison", boundary.Mutant.Mutator.ToString());
        Assert.Equal("Ages.cs", Path.GetFileName(boundary.Mutant.Location.FilePath));

        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal(0, report.Survived);
        Assert.Equal("100%", report.Score.ToString());
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

        // `age > 18` is indistinguishable from `age >= 18` once 18 itself is never tested.
        MutantResult boundary = Assert.Single(
            report.Results, result => result.Mutant.MutatedText == "age > 18");

        Assert.Equal(MutantStatus.Survived, boundary.Status);
        Assert.Equal(1, report.Survived);

        // `age < 18` is still caught: 42 is no longer an adult under it.
        MutantResult negation = Assert.Single(
            report.Results, result => result.Mutant.MutatedText == "age < 18");

        Assert.Equal(MutantStatus.Killed, negation.Status);
        Assert.Equal("50%", report.Score.ToString());
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
