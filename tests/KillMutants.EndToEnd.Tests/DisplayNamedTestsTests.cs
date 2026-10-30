using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A suite written as prose: <c>[Fact(DisplayName = "a customer over eighteen")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A test's display name is <c>Namespace.Class.Method</c> only by default. The coverage pass used to
/// list tests by display name and hand each one to <c>-method</c>, which takes the method; a sentence
/// selects nothing. Measured against just-dummies, whose 266 tests all carry a sentence: every mutant
/// the selection reached was "run" against no test at all and reported survived, while the same
/// mutant applied by hand failed two of its tests.
/// </para>
/// <para>
/// The property is the one the plain fixture already pins - selecting by coverage must not change a
/// verdict - held on the one suite where the two ways of naming a test come apart.
/// </para>
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class DisplayNamedTestsTests
{
    [Fact]
    public async Task Selecting_tests_by_coverage_gives_the_same_verdicts_when_the_tests_have_display_names()
    {
        using var selected = FixtureCopy.Create();
        using var everything = FixtureCopy.Create();
        selected.GiveTheTestsDisplayNames();
        everything.GiveTheTestsDisplayNames();

        MutationTestReport withSelection = await MutationTesting.RunAsync(
            selected.Root, cancellationToken: TestContext.Current.CancellationToken);

        MutationTestReport withoutSelection = await MutationTesting.RunAsync(
            everything.Root, measureCoverage: false,
            cancellationToken: TestContext.Current.CancellationToken);

        static string[] Verdicts(MutationTestReport report) =>
            [.. report.Results.Select(r => $"{r.Mutant.Id} {r.Mutant.MutatedText} {r.Status}")];

        // Not vacuous: a suite that killed nothing would agree with itself.
        Assert.Contains(withoutSelection.Results, result => result.Status == MutantStatus.Killed);
        Assert.Equal(Verdicts(withoutSelection), Verdicts(withSelection));
    }

    [Fact]
    public async Task A_killer_is_named_by_its_method_so_that_the_kill_can_be_reproduced()
    {
        using var fixture = FixtureCopy.Create();
        fixture.GiveTheTestsDisplayNames();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        MutantResult killed = report.Results.First(result => result.Status == MutantStatus.Killed);

        Assert.All(killed.KilledBy, name => Assert.StartsWith("Sample.Library.Tests.AgesTests.", name.ToString()));
    }

    /// <summary>
    /// The guard behind the fix above, held on its own: whatever a listing says, a selection that
    /// matches no test must not turn into a verdict.
    /// </summary>
    [Fact]
    public async Task A_selection_that_matches_no_test_runs_the_whole_suite_instead_of_reporting_survivors()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await new MutationTestSession(
                new NamesNothingItCanRun(new XUnitTestRunner()),
                "Release",
                timeoutPolicy: null,
                workerCount: 1,
                measureCoverage: true,
                exclude: null,
                catalog: null,
                verifyKills: 0)
            .RunAsync(fixture.Root, TestContext.Current.CancellationToken);

        Assert.Contains(report.Results, result => result.Status == MutantStatus.Killed);
        Assert.DoesNotContain(report.Results, result => result.Status == MutantStatus.Survived);
    }

    /// <summary>Lists the tests the way a display name would: as sentences no filter accepts.</summary>
    private sealed class NamesNothingItCanRun(ITestRunner inner) : ITestRunner
    {
        public async Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TestName> real = await inner.DiscoverAsync(testProject, cancellationToken);

            return [.. real.Select((_, index) => TestName.Create($"Sentence number {index}."))];
        }

        public Task<TestRunOutcome> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default) =>
            inner.RunAsync(request, cancellationToken);
    }
}
