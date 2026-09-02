using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What a partial run selects, and what it refuses to print. ADR-0010.
/// </summary>
/// <remarks>
/// Every one of these runs against a real git repository created inside the throwaway fixture, so
/// what is pinned is git's behaviour and not our reading of it. The <c>multi</c> fixture is used
/// because the interesting properties are all about telling one project from another:
/// <c>Core.Tests -&gt; Core</c> and <c>Domain.Tests -&gt; Domain -&gt; Core</c>.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class SinceRunTests
{
    /// <summary>
    /// Narrowed to two families, so a run costs seconds rather than a minute.
    /// </summary>
    /// <remarks>
    /// Both are needed to tell the two projects apart, and the first attempt at these tests used
    /// only <c>Comparison</c> and failed for that reason: <c>Basket.cs</c> holds a multiplication and
    /// a call and no comparison at all, so it produces no mutants under that family and its absence
    /// says nothing about what was selected.
    /// </remarks>
    private static readonly MutatorName[] Families =
    [
        MutatorName.Create("Comparison"),
        MutatorName.Create("Arithmetic"),
    ];

    /// <summary>The precise half: changed production code, and only that.</summary>
    [Fact]
    public async Task A_changed_source_file_selects_its_own_mutants_and_no_others()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Equal(["Money.cs"], MutatedFiles(report));
        Assert.NotEmpty(report.Results);
    }

    /// <summary>
    /// The report has to say which population was inspected, or it cannot be told from a full run.
    /// </summary>
    [Fact]
    public async Task The_report_records_the_run_mode_and_the_revisions_it_resolved()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.True(report.Scope.IsPartial);

        // Resolved, not as the run was asked for them: 'HEAD' names a different commit tomorrow.
        Assert.Equal(40, report.Scope.BaseRevision?.Length);
        Assert.Equal(40, report.Scope.HeadRevision?.Length);
        Assert.True(report.Scope.WorkingTreeDiffers);
        Assert.True(report.Scope.ChangedFiles > 0);
    }

    /// <summary>
    /// The conservative half: a change to an existing test file puts no production code in the diff
    /// at all, and the mutants that test used to kill have to be judged anyway.
    /// </summary>
    /// <remarks>
    /// What a change to a test removes is a coverage edge, and HEAD cannot be asked about an edge
    /// that is no longer there - so the selection widens to every project the suite exercises, from
    /// project references rather than from observed coverage. <c>Domain.Tests</c> reaches
    /// <c>Domain</c> and, through it, <c>Core</c>.
    /// </remarks>
    [Fact]
    public async Task A_changed_test_file_widens_to_what_that_suite_exercises()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Domain.Tests", "BasketTests.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// The relation is read at the base revision too, or the same hole reappears one layer down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First the vanishing relation was <c>T -&gt; M</c>; here it is <c>Tests -&gt; ProjectA</c>.
    /// This change removes <c>Domain.Tests</c>' project reference to <c>Domain</c> and rewrites its
    /// test, so at HEAD that suite exercises nothing at all: asking HEAD which projects it covers is
    /// asking a question whose answer the change has already deleted.
    /// </para>
    /// <para>
    /// At the base revision it reached <c>Domain</c> and, through it, <c>Core</c>. <c>Domain</c> is
    /// gone from the run - nothing reaches it any more, so it is not a target and cannot be widened
    /// to - but <c>Core</c> is still one, through <c>Core.Tests</c>, and its mutants have to be
    /// judged. Without the base graph this run selects nothing whatsoever.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_test_project_that_stopped_referencing_a_project_is_still_read_at_the_base()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        string project = Path.Combine(fixture.Root, "Domain.Tests", "Domain.Tests.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                """<ItemGroup><ProjectReference Include="../Domain/Domain.csproj" /></ItemGroup>""",
                string.Empty,
                StringComparison.Ordinal));

        // Rewritten rather than merely touched: with the reference gone, the tests it held would not
        // compile, and the run would stop at the build long before the selection mattered.
        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain.Tests", "BasketTests.cs"),
            """
            namespace Domain.Tests;

            public class BasketTests
            {
                [Fact]
                public void Nothing_is_asserted_about_the_basket_any_more() => Assert.True(true);
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A test the change <em>added</em> cannot have removed an edge that predates it, so it widens
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The distinction matters more than it looks: adding a test is the commonest shape a pull
    /// request has, and widening on it would make every such run a full one.
    /// </remarks>
    [Fact]
    public async Task An_added_test_file_widens_nothing()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain.Tests", "AnotherBasketTests.cs"),
            """
            namespace Domain.Tests;

            public class AnotherBasketTests
            {
                [Fact]
                public void An_empty_basket_costs_nothing() => Assert.Equal(0, 0);
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Empty(report.Results);
        Assert.True(report.Scope.IsPartial);
    }

    /// <summary>
    /// A change that touches nothing a build reads produces an empty run, and an empty run passes.
    /// </summary>
    [Fact]
    public async Task A_change_to_documentation_alone_selects_nothing()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        File.WriteAllText(Path.Combine(fixture.Root, "README.md"), "Nothing a compiler reads.");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Empty(report.Results);
        Assert.False(report.HasUndetected);
        Assert.False(report.IsInconclusive);
    }

    /// <summary>
    /// Code the change adds and nothing tests is <c>NoCoverage</c>, not <c>Survived</c>, and the
    /// verdict has to fail on it.
    /// </summary>
    /// <remarks>
    /// A gate reading only survivors would wave through the clearest case of newly introduced
    /// untested behaviour there is, which is the one thing this run exists to catch.
    /// </remarks>
    [Fact]
    public async Task Untested_code_the_change_adds_fails_the_verdict()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        string source = Path.Combine(fixture.Root, "Core", "Money.cs");

        File.WriteAllText(
            source,
            File.ReadAllText(source) +
            """

            public static class Discounts
            {
                public static bool IsWorthIt(int saving, int threshold) => saving >= threshold;
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.True(report.HasUndetected);
        Assert.True(report.Uncovered > 0);
        Assert.False(report.IsInconclusive);
    }

    /// <summary>
    /// A full run is unchanged by any of this: no scope, and a score as before.
    /// </summary>
    [Fact]
    public async Task A_run_without_the_option_still_measures_the_whole_codebase()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: Families,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.Scope.IsPartial);
        Assert.Contains("Basket.cs", MutatedFiles(report));
        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    private static Task<MutationTestReport> RunSinceHeadAsync(FixtureCopy fixture) =>
        MutationTesting.RunAsync(
            fixture.Root,
            mutators: Families,
            since: "HEAD",
            cancellationToken: TestContext.Current.CancellationToken);

    private static void Touch(FixtureCopy fixture, params string[] parts)
    {
        string path = Path.Combine([fixture.Root, .. parts]);

        File.AppendAllText(path, $"{Environment.NewLine}// touched{Environment.NewLine}");
    }

    private static string[] MutatedFiles(MutationTestReport report) =>
    [
        .. report.Results
            .Select(result => Path.GetFileName(result.Mutant.Location.FilePath))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}
