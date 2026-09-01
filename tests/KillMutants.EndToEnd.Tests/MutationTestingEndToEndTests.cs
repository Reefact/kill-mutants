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
    /// Milestone 5. Code no test reaches produces mutants that could only ever be reported as
    /// survived - which would read as a gap in the tests rather than as their absence. They are
    /// recorded as NoCoverage instead, never run, and excluded from the score.
    /// </summary>
    [Fact]
    public async Task Mutants_in_code_no_test_reaches_are_reported_as_uncovered()
    {
        using var fixture = FixtureCopy.Create();
        fixture.AddCodeNoTestReaches();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        MutantResult[] uncovered = [.. report.Results.Where(r => r.Status == MutantStatus.NoCoverage)];

        Assert.NotEmpty(uncovered);
        Assert.All(
            uncovered,
            result => Assert.Equal("Untested.cs", Path.GetFileName(result.Mutant.Location.FilePath)));

        // Everything the tests do reach is still killed, and the untestable mutants do not drag the
        // score down: a mutant that cannot be tested says nothing about the quality of the tests.
        Assert.All(
            report.Results.Where(r => r.Status != MutantStatus.NoCoverage),
            result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal("100%", report.Score.ToString());
    }

    /// <summary>
    /// The property that makes test selection safe: running only the tests that reach a mutant must
    /// reach the same verdict as running all of them.
    /// </summary>
    [Fact]
    public async Task Selecting_tests_by_coverage_gives_the_same_verdicts_as_running_them_all()
    {
        using var selected = FixtureCopy.Create();
        using var everything = FixtureCopy.Create();

        MutationTestReport withSelection = await MutationTesting.RunAsync(
            selected.Root, cancellationToken: TestContext.Current.CancellationToken);

        MutationTestReport withoutSelection = await MutationTesting.RunAsync(
            everything.Root, measureCoverage: false,
            cancellationToken: TestContext.Current.CancellationToken);

        static string[] Verdicts(MutationTestReport report) =>
            [.. report.Results.Select(r => $"{r.Mutant.Id} {r.Mutant.MutatedText} {r.Status}")];

        Assert.Equal(Verdicts(withoutSelection), Verdicts(withSelection));
    }

    /// <summary>
    /// Milestone 6's correctness property, and the one that matters more than its speed: running
    /// mutants concurrently must not change a single verdict. Each worker owns a private copy of the
    /// test output directory, so no two mutants can ever see each other's assembly - the failure
    /// mode that makes shared, warmed-up test hosts unsafe.
    /// </summary>
    [Fact]
    public async Task Testing_mutants_in_parallel_gives_exactly_the_same_verdicts()
    {
        using var sequentialFixture = FixtureCopy.Create();
        using var parallelFixture = FixtureCopy.Create();

        MutationTestReport sequential = await MutationTesting.RunAsync(
            sequentialFixture.Root, workerCount: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        MutationTestReport concurrent = await MutationTesting.RunAsync(
            parallelFixture.Root, workerCount: 4,
            cancellationToken: TestContext.Current.CancellationToken);

        static string[] Verdicts(MutationTestReport report) =>
            [.. report.Results.Select(result =>
                $"{result.Mutant.Id} {result.Mutant.Location} {result.Mutant.MutatedText} {result.Status}")];

        // Identical, and in the same order: results are re-sequenced after the workers finish, so a
        // report never depends on which worker happened to finish first.
        Assert.Equal(Verdicts(sequential), Verdicts(concurrent));
        Assert.Equal(sequential.Score.ToString(), concurrent.Score.ToString());
    }

    /// <summary>
    /// Milestone 3. Two libraries and two test suites, where Core is reached by both suites - once
    /// directly and once through Domain - and Domain only by its own.
    /// </summary>
    [Fact]
    public async Task Several_projects_and_several_test_suites_are_all_covered()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        string[] mutatedFiles = [.. report.Results
            .Select(result => Path.GetFileName(result.Mutant.Location.FilePath))
            .Distinct()
            .Order(StringComparer.Ordinal)];

        // Both libraries are mutated. Basket.cs is only reachable from its own suite; Money.cs is
        // reached transitively from Domain.Tests as well, and its mutants are injected into both
        // suites' output directories so either can kill them.
        Assert.Equal(["Basket.cs", "Money.cs"], mutatedFiles);

        // The test projects are the yardstick, never the thing measured.
        Assert.DoesNotContain(report.Results, result =>
            result.Mutant.Location.FilePath.Contains(".Tests", StringComparison.Ordinal));

        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
        Assert.Equal("100%", report.Score.ToString());

        // Identifiers run across the whole session rather than restarting for each project.
        Assert.Equal(
            report.Results.Select(result => result.Mutant.Id.ToString()).Distinct().Count(),
            report.Total);
    }

    /// <summary>
    /// Closes RB-002. Without running the generators, this project cannot be compiled at all: the
    /// partial property has no implementation and the emit fails with CS9248, which reads as a
    /// defect in KillMutants rather than a missing step.
    /// </summary>
    [Fact]
    public async Task A_project_that_depends_on_a_source_generator_is_mutated_and_tested()
    {
        using var fixture = FixtureCopy.Create();
        fixture.UseCodeThatDependsOnASourceGenerator();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Results);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));

        // Only the developer's own code is mutated. The generated regex engine is full of
        // comparisons and arithmetic; mutating it would report findings against code nobody wrote.
        Assert.All(
            report.Results,
            result => Assert.Equal("Ages.cs", Path.GetFileName(result.Mutant.Location.FilePath)));

        // The mutants come from the hand-written expression: a comparison and a logical operator.
        Assert.Equal(
            ["Comparison", "LogicalOperator"],
            report.Results.Select(r => r.Mutant.Mutator.ToString()).Distinct().Order(StringComparer.Ordinal));
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
