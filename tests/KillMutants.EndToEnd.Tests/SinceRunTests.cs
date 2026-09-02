using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What a partial run selects (DEC0011), and what it refuses to print (DEC0010).
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

        // And Domain, which that suite was the only one to reach, is gone from the run entirely.
        // There are no mutants to report for it - nothing reaches it, so no suite could judge one -
        // which is exactly how deleting a component's last test came to read as a clean pass.
        Assert.True(report.LostCoverage);
        Assert.Contains(
            report.CoverageLost,
            path => path.EndsWith("Domain.csproj", StringComparison.Ordinal));
    }

    /// <summary>
    /// A project the run was told to leave alone has not lost its coverage; it never had any here.
    /// </summary>
    /// <remarks>
    /// The other half of the rule above, and the one that keeps it from crying wolf. Excluding
    /// <c>Domain</c> takes it out of the targets exactly as removing its last test reference would,
    /// and the two must not be reported the same way: one is the user's instruction, the other is a
    /// change deleting a component's coverage behind their back.
    /// </remarks>
    [Fact]
    public async Task A_project_the_run_excludes_is_not_reported_as_having_lost_coverage()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Domain.Tests", "BasketTests.cs");

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Domain/*"],
            mutators: Families,
            since: "HEAD",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.LostCoverage);
    }

    /// <summary>
    /// A build file above the suites reaches them, even when no production project sits beneath it.
    /// </summary>
    /// <remarks>
    /// Review found this. A <c>tests/Directory.Build.props</c> sits above every suite and beneath no
    /// production project, so widening only what was physically under it widened nothing at all in
    /// the ordinary <c>src/</c> and <c>tests/</c> layout - and such a file can remove a source from a
    /// suite's compilation as surely as deleting it would.
    /// </remarks>
    [Fact]
    public async Task A_build_file_above_a_test_project_widens_what_that_suite_exercises()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        MoveIntoTestsDirectory(fixture, "Domain.Tests");

        string props = Path.Combine(fixture.Root, "tests", "Directory.Build.props");

        // Repeats what the fixture's root file says, because MSBuild stops walking up at the first
        // Directory.Build.props it finds and this one now shadows it for everything beneath.
        File.WriteAllText(
            props,
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        FixtureRepository.InitialiseAt(fixture.Root);
        File.AppendAllText(props, $"{Environment.NewLine}<!-- touched -->{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A suite can be switched off without being deleted, and that is just as much a loss of
    /// coverage.
    /// </summary>
    /// <remarks>
    /// Review found this. The base side used to be consulted only for projects whose file no longer
    /// exists at HEAD, so flipping a test project's <c>OutputType</c> - which stops it being a test
    /// project without moving a single file - left its changed project file attributed to nothing.
    /// With another suite still reaching the same production code, discovery succeeds and the run
    /// goes green over a suite that has been disabled.
    /// </remarks>
    [Fact]
    public async Task A_test_project_that_stopped_being_one_is_still_read_at_the_base()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        string project = Path.Combine(fixture.Root, "Domain.Tests", "Domain.Tests.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "<OutputType>Exe</OutputType>", "<OutputType>Library</OutputType>",
                StringComparison.Ordinal));

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A test project reaches out of its own folder for a file, and a change to it is still
    /// test-side.
    /// </summary>
    /// <remarks>
    /// Review found this too. Attribution was by directory alone, so a file included with a
    /// <c>Link</c> from outside the project read as production code - and since a test project's
    /// compilation is never mutated, deleting an assertion from it would have produced an empty,
    /// passing run. Evaluated membership is the authoritative answer, and it is asked when the
    /// directory has none.
    /// </remarks>
    [Fact]
    public async Task A_changed_file_a_test_project_links_in_from_elsewhere_widens_too()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        Directory.CreateDirectory(Path.Combine(fixture.Root, "SharedTests"));

        string linked = Path.Combine(fixture.Root, "SharedTests", "Assertions.cs");

        File.WriteAllText(
            linked,
            """
            namespace Domain.Tests;

            public static class Assertions
            {
                public static void IsTwelve(int value) => Assert.Equal(12, value);
            }
            """);

        string project = Path.Combine(fixture.Root, "Domain.Tests", "Domain.Tests.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "</Project>",
                """
                  <ItemGroup>
                    <Compile Include="../SharedTests/Assertions.cs" Link="Assertions.cs" />
                  </ItemGroup>
                </Project>
                """,
                StringComparison.Ordinal));

        FixtureRepository.InitialiseAt(fixture.Root);

        File.AppendAllText(linked, $"{Environment.NewLine}// touched{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A submodule bump changes what gets built, and git reports it as one directory.
    /// </summary>
    /// <remarks>
    /// Measured before it was fixed: <c>git diff --name-status</c> emits <c>M libs/Core</c> - the
    /// gitlink path, with nothing beneath it - so every rule that reads a file's parent directory
    /// looked at the wrong project, the change was attributed to nothing, and a submodule-only code
    /// update produced an empty, passing run over the code it had just replaced.
    /// </remarks>
    [Fact]
    public async Task A_changed_submodule_widens_every_project_beneath_it()
    {
        using var outer = FixtureCopy.CreateMultiProject();
        using var inner = FixtureCopy.Create();

        FixtureRepository.InitialiseAt(inner.Root);
        FixtureRepository.InitialiseAt(outer.Root);
        FixtureRepository.AddSubmodule(outer.Root, inner.Root, "libs/Sample");

        // The bump: the submodule's own history moves on, and the outer repository records the new
        // commit. Nothing inside libs/Sample appears in the outer diff.
        File.AppendAllText(
            Path.Combine(inner.Root, "Sample.Library", "Ages.cs"),
            $"{Environment.NewLine}// moved on{Environment.NewLine}");

        FixtureRepository.CommitAll(inner.Root, "the submodule moves on");
        FixtureRepository.BumpSubmodule(outer.Root, "libs/Sample");

        MutationTestReport report = await RunSinceHeadAsync(outer);

        Assert.Contains("Ages.cs", MutatedFiles(report));
    }

    /// <summary>
    /// Two projects can share a directory, and a partial run has to survive it.
    /// </summary>
    /// <remarks>
    /// Review found this. Ownership was a dictionary keyed by directory, so two mutable projects in
    /// one folder threw a duplicate-key exception before a single change had been classified: every
    /// partial run in such a repository died where a full run works, which is the worst shape a
    /// limitation can take. A directory now holds all its projects.
    /// </remarks>
    [Fact]
    public async Task Two_projects_in_one_directory_do_not_stop_a_partial_run()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        File.WriteAllText(
            Path.Combine(fixture.Root, "Core", "Extra.cs"),
            """
            namespace Core.Extra;

            public static class Rounding
            {
                public static bool IsWhole(int cents) => cents % 100 == 0;
            }
            """);

        // Two projects in one folder, each compiling its own file: the default glob would otherwise
        // have both of them compile both files, which is a different situation entirely.
        File.WriteAllText(
            Path.Combine(fixture.Root, "Core", "Extra.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Core.Extra</AssemblyName>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup><Compile Include="Extra.cs" /></ItemGroup>
            </Project>
            """);

        Exclude(fixture, "Core", "Core.csproj", "Extra.cs");
        Reference(fixture, "Core.Tests", "Core.Tests.csproj", "../Core/Extra.csproj");

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Equal(["Money.cs"], MutatedFiles(report));
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

    /// <summary>
    /// Moves a test project one level down, into a <c>tests/</c> directory of its own.
    /// </summary>
    /// <remarks>
    /// The <c>multi</c> fixture is flat, so a shared build file at its root sits above the production
    /// projects too and widens them whatever the rule. Reproducing the case at all needs a directory
    /// that holds suites and no production project, which is the ordinary layout of a real
    /// repository.
    /// </remarks>
    private static void MoveIntoTestsDirectory(FixtureCopy fixture, string project)
    {
        string from = Path.Combine(fixture.Root, project);
        string to = Path.Combine(fixture.Root, "tests", project);

        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from))
        {
            // One directory deeper, so every relative project reference gains a level.
            File.WriteAllText(
                Path.Combine(to, Path.GetFileName(file)),
                File.ReadAllText(file).Replace("\"../", "\"../../", StringComparison.Ordinal));
        }

        Directory.Delete(from, recursive: true);
    }

    private static void Exclude(FixtureCopy fixture, string directory, string project, string file)
    {
        string path = Path.Combine(fixture.Root, directory, project);

        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(
                "</Project>",
                $"  <ItemGroup><Compile Remove=\"{file}\" /></ItemGroup>{Environment.NewLine}</Project>",
                StringComparison.Ordinal));
    }

    private static void Reference(FixtureCopy fixture, string directory, string project, string reference)
    {
        string path = Path.Combine(fixture.Root, directory, project);

        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(
                "</Project>",
                $"  <ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup>" +
                $"{Environment.NewLine}</Project>",
                StringComparison.Ordinal));
    }

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
