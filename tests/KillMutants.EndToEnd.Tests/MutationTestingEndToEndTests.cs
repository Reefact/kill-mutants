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

        MutantResult result = Assert.Single(report.Results);

        Assert.Equal(MutantStatus.Killed, result.Status);
        Assert.Equal("age >= 18", result.Mutant.OriginalText);
        Assert.Equal("age > 18", result.Mutant.MutatedText);
        Assert.Equal("GreaterThanOrEqual", result.Mutant.Mutator.ToString());
        Assert.Equal("Ages.cs", Path.GetFileName(result.Mutant.Location.FilePath));

        Assert.Equal(1, report.Total);
        Assert.Equal(1, report.Killed);
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

        MutantResult result = Assert.Single(report.Results);

        Assert.Equal(MutantStatus.Survived, result.Status);
        Assert.Equal(0, report.Killed);
        Assert.Equal(1, report.Survived);
        Assert.Equal("0%", report.Score.ToString());
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

        MutantResult result = Assert.Single(report.Results);

        Assert.Equal(MutantStatus.Killed, result.Status);
        Assert.Equal("100%", report.Score.ToString());
    }

    [Fact]
    public async Task The_report_renders_the_milestone_output()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        var writer = new StringWriter();
        ConsoleReportWriter.Write(writer, report);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "KillMutants",
                string.Empty,
                "Mutants: 1",
                "Killed: 1",
                "Survived: 0",
                string.Empty,
                "Mutation score: 100%",
                string.Empty),
            writer.ToString());
    }
}

/// <summary>End-to-end runs build real projects, so they run one at a time.</summary>
[CollectionDefinition(nameof(SerialEndToEnd), DisableParallelization = true)]
public class SerialEndToEnd;
