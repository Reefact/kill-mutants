using KillMutants.Git;
using KillMutants.Mutations;
using KillMutants.Reporting;
using KillMutants.Selection;

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
    public async Task The_report_records_the_run_mode_and_the_states_it_compared()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.True(report.Scope.IsPartial);

        // Resolved by the source, not as the run was asked for them: 'HEAD' names something else
        // tomorrow. Asserted as full-length git object names, because that is what this source
        // supplies - the core itself makes no claim about their shape.
        Assert.Equal(40, report.Scope.ComparedFrom?.Length);
        Assert.Equal(40, report.Scope.ComparedTo?.Length);

        // The fixture has uncommitted work, so what was measured is not exactly the named state.
        Assert.False(report.Scope.ComparedToIsExact);
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
            changes: await ChangesSinceHeadAsync(fixture.Root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.LostCoverage);
    }

    /// <summary>
    /// An excluded project stays excluded when the change removes the last reference to it.
    /// </summary>
    /// <remarks>
    /// Review found this, and it is the only false positive the gate had: a run that fails over a
    /// project the user deliberately took out of mutation testing. The two rules above met in a case
    /// neither covered. Being left out was read from what a traversal <em>now</em> still reaches, so
    /// a change that removes the last edge to an excluded project left nothing to read it from, and
    /// the project came back through the earlier state's graph as newly uncovered. It was never
    /// covered: the run was told to leave it alone, before this change and after it.
    /// </remarks>
    [Fact]
    public async Task An_excluded_project_whose_last_reference_the_change_removes_is_not_reported()
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

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Domain/*"],
            mutators: Families,
            changes: await ChangesSinceHeadAsync(fixture.Root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.LostCoverage);
        Assert.Empty(report.CoverageLost);
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
    /// A file one suite owns by directory and another links in widens both.
    /// </summary>
    /// <remarks>
    /// Review found that the two attribution rules were alternatives rather than a union: the
    /// directory's owners were returned as soon as there were any, so membership was never consulted
    /// and the linking suite was never widened. Production code only that suite exercises would have
    /// stayed out of the run.
    /// </remarks>
    [Fact]
    public async Task A_file_one_suite_owns_and_another_links_widens_both()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        string shared = Path.Combine(fixture.Root, "Core.Tests", "Assertions.cs");

        File.WriteAllText(
            shared,
            """
            namespace Shared.Assertions;

            public static class Amounts
            {
                public static void IsTwelve(int value) => Assert.Equal(12, value);
            }
            """);

        // Owned by Core.Tests because it sits there, and by Domain.Tests because it links it in.
        Reference(
            fixture,
            "Domain.Tests",
            "Domain.Tests.csproj",
            null,
            """<Compile Include="../Core.Tests/Assertions.cs" Link="Assertions.cs" />""");

        FixtureRepository.InitialiseAt(fixture.Root);
        File.AppendAllText(shared, $"{Environment.NewLine}// touched{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        // Money.cs would come from the directory owner alone. Basket.cs is the half that only the
        // union provides: Domain is exercised by Domain.Tests and by nothing else here.
        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A production file inside a suite's folder is still production code.
    /// </summary>
    /// <remarks>
    /// Review found this. Test ownership was checked first and always moved on, so in a nested layout
    /// - a fixture library inside the suite's own directory - an added production file was attributed
    /// to the enclosing suite, and an added file widens nothing. Newly added untested code therefore
    /// produced an empty, passing run, which is the one case this feature exists to catch.
    /// </remarks>
    [Fact]
    public async Task A_production_file_inside_a_test_projects_folder_is_still_selected()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        string library = Path.Combine(fixture.Root, "Domain.Tests", "FixtureLib");

        Directory.CreateDirectory(library);

        File.WriteAllText(
            Path.Combine(library, "FixtureLib.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(library, "Clock.cs"), "namespace FixtureLib;\n\npublic static class Clock { }\n");

        // The suite's default glob would otherwise compile the library's sources as well, which is a
        // different situation from the nested one under test.
        Exclude(fixture, "Domain.Tests", "Domain.Tests.csproj", "FixtureLib/**");
        Reference(fixture, "Domain.Tests", "Domain.Tests.csproj", "FixtureLib/FixtureLib.csproj");

        FixtureRepository.InitialiseAt(fixture.Root);

        // Added, inside the suite's directory, and exercised by nothing.
        File.WriteAllText(
            Path.Combine(library, "Rounding.cs"),
            """
            namespace FixtureLib;

            public static class Rounding
            {
                public static bool IsWhole(int cents) => cents % 100 == 0;
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Rounding.cs", MutatedFiles(report));
        Assert.True(report.HasUndetected);
    }

    /// <summary>
    /// A change to a declared test-support library is a change to what its suites can see.
    /// </summary>
    /// <remarks>
    /// Review found this. Support is deliberately not a target, so a changed file in it was added to
    /// the selected files and then matched no compilation at all: a helper change that stops tests
    /// reaching production behaviour selected nothing and passed. It is test-side, and widens through
    /// the suites that reach it.
    /// </remarks>
    [Fact]
    public async Task A_change_in_a_declared_support_library_widens_what_its_suites_exercise()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();

        fixture.DeclareTheSupportProject();
        FixtureRepository.InitialiseAt(fixture.Root);

        File.AppendAllText(
            Path.Combine(fixture.Root, "Sample.Support", "Affordability.cs"),
            $"{Environment.NewLine}// touched{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// An added file in a support library is not an added test either.
    /// </summary>
    /// <remarks>
    /// Review found the last place the exception reached that it had no business reaching. "A new
    /// test cannot remove an edge that predates it" is an argument about tests, and a declared
    /// support library is explicitly not a test project - its files are helpers every suite compiles
    /// against. A module initializer, an assembly attribute or a new part of an existing partial
    /// helper changes what the existing tests do, with no test file in the diff at all. The same
    /// reasoning already kept the exception off the generator path; it belongs off this one too.
    /// </remarks>
    [Fact]
    public async Task An_added_file_in_a_support_library_widens_even_though_it_is_added()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();

        fixture.DeclareTheSupportProject();
        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(
            Path.Combine(fixture.Root, "Sample.Support", "Defaults.cs"),
            """
            using System.Runtime.CompilerServices;

            namespace Sample.Support;

            internal static class Defaults
            {
                internal static bool Ready;

                [ModuleInitializer]
                internal static void Prepare() => Ready = true;
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// An added fixture is not an added test, and the exception does not stretch to cover it.
    /// </summary>
    /// <remarks>
    /// Review found the overreach. "A new test cannot remove an edge that predates it" is true of a
    /// test; a case list, a settings file or any other input an existing test reads can change what
    /// that test does, and adding one can remove a coverage edge with no production file in the diff
    /// at all. Only an added C# file keeps the exception now.
    /// </remarks>
    [Fact]
    public async Task An_added_fixture_in_a_test_project_widens_even_though_it_is_added()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain.Tests", "cases.json"),
            """{ "budget": 12 }""");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A resource a project reaches out of its own folder for is still that project's input.
    /// </summary>
    /// <remarks>
    /// Review found this as the production-side twin of the linked source: a non-C# file with no
    /// enclosing project and no shared-build-file name fell through to nothing, even though the
    /// project that embeds it would build differently. Attribution is by what a project evaluates,
    /// which sees it.
    /// </remarks>
    [Fact]
    public async Task A_linked_resource_outside_a_project_widens_the_project_that_embeds_it()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        Directory.CreateDirectory(Path.Combine(fixture.Root, "Shared"));

        string resource = Path.Combine(fixture.Root, "Shared", "Strings.txt");

        File.WriteAllText(resource, "one");

        Reference(
            fixture,
            "Domain",
            "Domain.csproj",
            null,
            """<EmbeddedResource Include="../Shared/Strings.txt" Link="Strings.txt" />""");

        FixtureRepository.InitialiseAt(fixture.Root);
        File.WriteAllText(resource, "two");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A partial run cannot judge a change to the settings that decide what it measures.
    /// </summary>
    /// <remarks>
    /// Review found the sharp version: `exclude` in killmutants.json takes effect in discovery,
    /// before any selection exists, so a change adding one removes a project from the targets and no
    /// widening afterwards can reach it. A pull request could switch mutation testing off for a
    /// component by editing the file that configures the gate. The run declines instead.
    /// </remarks>
    [Fact]
    public async Task A_change_to_the_run_configuration_is_refused()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(
            Path.Combine(fixture.Root, "killmutants.json"),
            """{ "exclude": ["Domain/*"] }""");

        ChangeSelectionException refusal = await Assert.ThrowsAsync<ChangeSelectionException>(
            () => RunSinceHeadAsync(fixture));

        Assert.Contains("killmutants.json", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--since", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project marked <c>export-ignore</c> is read as it was, not refused and not read as absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserted a refusal, and the refusal was the wrong answer to the right observation.
    /// Review had found that <c>git archive</c> is an unfaithful copy of a revision - it honours
    /// <c>export-ignore</c>, a common way to keep tests out of a release archive - so a project
    /// could be silently absent and the graph would drop the edge running through it. Detecting
    /// that and stopping the run was a way of not lying about it.
    /// </para>
    /// <para>
    /// Reading the code properly is better than refusing to. <c>export-ignore</c> shapes an archive
    /// and means nothing to a checkout, so a worktree simply has the project. What was a refusal is
    /// now an ordinary run, and this asserts the reading: <c>Domain.Tests</c> touched, and
    /// <c>Basket.cs</c> - which lives in the directory the attribute excludes from archives - judged
    /// through the base graph like any other.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_project_marked_export_ignore_is_still_read_as_it_was()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        File.WriteAllText(Path.Combine(fixture.Root, ".gitattributes"), "Domain/ export-ignore\n");

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Domain.Tests", "BasketTests.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A support library that a change stops reaching has not lost coverage it never had.
    /// </summary>
    /// <remarks>
    /// Review found the third instance of one rule in this codebase: a declared test-support library
    /// is a hole in the graph, not a wall - walked through, never returned. The base traversal
    /// returned it as production code, so when a change removed the suite that reached it, the
    /// coverage-loss check reported it as a component whose tests had gone and failed the gate over a
    /// project that was never a target.
    /// </remarks>
    [Fact]
    public async Task A_support_library_the_change_stops_reaching_is_not_reported_as_lost()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();

        fixture.DeclareTheSupportProject();

        // A second suite, reaching the library directly, so the library itself stays covered when the
        // first suite lets go of the support project.
        string other = Path.Combine(fixture.Root, "Sample.Other.Tests");

        Directory.CreateDirectory(other);

        File.WriteAllText(
            Path.Combine(other, "Sample.Other.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup><Using Include="Xunit" /></ItemGroup>
              <ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.0" /></ItemGroup>
              <ItemGroup><ProjectReference Include="../Sample.Library/Sample.Library.csproj" /></ItemGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(other, "OtherMoneyTests.cs"),
            """
            using Sample.Library;

            namespace Sample.Other.Tests;

            public class OtherMoneyTests
            {
                [Fact]
                public void A_price_within_budget_is_affordable() =>
                    Assert.True(Money.IsAffordable(price: 5, budget: 10));
            }
            """);

        FixtureRepository.InitialiseAt(fixture.Root);

        // The first suite lets go of the support project, and rewrites the test that needed it.
        string project = Path.Combine(fixture.Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                """<ItemGroup><ProjectReference Include="../Sample.Support/Sample.Support.csproj" /></ItemGroup>""",
                """<ItemGroup><ProjectReference Include="../Sample.Library/Sample.Library.csproj" /></ItemGroup>""",
                StringComparison.Ordinal));

        File.WriteAllText(
            Path.Combine(fixture.Root, "Sample.Library.Tests", "MoneyTests.cs"),
            """
            using Sample.Library;

            namespace Sample.Library.Tests;

            public class MoneyTests
            {
                [Fact]
                public void A_price_above_budget_is_not() =>
                    Assert.False(Money.IsAffordable(price: 11, budget: 10));
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.False(report.LostCoverage);
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
    /// A component reported as one opaque path carries the roles its projects hold from outside it.
    /// </summary>
    /// <remarks>
    /// Review found this, and it is the gap the rule above left. Widening beneath the path knows two
    /// roles - a target that sits under it, a suite that sits under it - which is everything when the
    /// component is self-contained. A project inside it that something <em>outside</em> reaches has
    /// neither: a library the run leaves out, or a generator, consumed from elsewhere in the tree. So
    /// the component moved, the code it contributes changed, and nothing was selected.
    /// <para>
    /// Here <c>Domain.Tests</c> reaches into the submodule for a library the run excludes. Nothing
    /// beneath <c>libs/Sample</c> is a target and no suite lives there, so the two loops find
    /// nothing; the suite outside is what has to be marked touched, exactly as a changed file inside
    /// that library would mark it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_changed_component_widens_through_a_suite_that_reaches_into_it()
    {
        using var outer = FixtureCopy.CreateMultiProject();
        using var inner = FixtureCopy.Create();

        FixtureRepository.InitialiseAt(inner.Root);
        FixtureRepository.InitialiseAt(outer.Root);
        FixtureRepository.AddSubmodule(outer.Root, inner.Root, "libs/Sample");

        // Committed before the bump, so the only thing in the diff is the submodule's own move.
        string suite = Path.Combine(outer.Root, "Domain.Tests", "Domain.Tests.csproj");

        File.WriteAllText(
            suite,
            File.ReadAllText(suite).Replace(
                """<ItemGroup><ProjectReference Include="../Domain/Domain.csproj" /></ItemGroup>""",
                """
                <ItemGroup>
                  <ProjectReference Include="../Domain/Domain.csproj" />
                  <ProjectReference Include="../libs/Sample/Sample.Library/Sample.Library.csproj" />
                </ItemGroup>
                """,
                StringComparison.Ordinal));

        FixtureRepository.CommitAll(outer.Root, "the suite reaches into the component");

        File.AppendAllText(
            Path.Combine(inner.Root, "Sample.Library", "Ages.cs"),
            $"{Environment.NewLine}// moved on{Environment.NewLine}");

        FixtureRepository.CommitAll(inner.Root, "the component moves on");
        FixtureRepository.BumpSubmodule(outer.Root, "libs/Sample");

        MutationTestReport report = await MutationTesting.RunAsync(
            outer.Root,
            exclude: ["libs/*"],
            mutators: Families,
            changes: await ChangesSinceHeadAsync(outer.Root),
            cancellationToken: TestContext.Current.CancellationToken);

        // What Domain.Tests exercises, widened because the suite is what the component reaches.
        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// What a project consumes is read for the framework its tests actually load.
    /// </summary>
    /// <remarks>
    /// Review found this, and measuring it settled it. MSBuild evaluated without a
    /// <c>TargetFramework</c> is the outer build of a multi-targeted project, where
    /// <c>$(TargetFramework)</c> is empty and every item conditioned on it is simply absent:
    /// asked for <c>AdditionalFiles</c>, the outer evaluation of this fixture answers with the
    /// unconditioned file alone, while <c>-p:TargetFramework=net10.0</c> answers with both.
    /// <para>
    /// So a file the mutated compilation genuinely reads was invisible to the evaluation that
    /// records what each project consumes, and changing it was attributed to nothing at all. The
    /// facts resolved for the framework a suite loads are now recorded too, which is the same
    /// evaluation the compilation under test comes from.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_framework_conditional_input_is_attributed_to_the_project_that_reads_it()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedProject();

        Directory.CreateDirectory(Path.Combine(fixture.Root, "shared"));
        File.WriteAllText(Path.Combine(fixture.Root, "shared", "options.json"), "{}");

        string project = Path.Combine(fixture.Root, "Sample.Library", "Sample.Library.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "</Project>",
                """
                  <ItemGroup>
                    <AdditionalFiles Include="../shared/options.json"
                                     Condition="'$(TargetFramework)' == 'net10.0'" />
                  </ItemGroup>
                </Project>
                """,
                StringComparison.Ordinal));

        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(
            Path.Combine(fixture.Root, "shared", "options.json"),
            """{ "rounding": "up" }""");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Ages.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A suite that stops being one is read as a former suite even when it becomes something else.
    /// </summary>
    /// <remarks>
    /// Review found this. Whether a project stopped being a suite is a question about the earlier
    /// state, and it was being asked only of changes nothing else had claimed. A project file that
    /// disables its own suite usually claims nothing - which is why the rule worked - but the same
    /// edit can hand the project another role in the same breath: <c>Domain.Tests</c> stops being a
    /// test project and becomes a target for the suite that already referenced it. Its change is
    /// then attributed to that target, and the question is never asked.
    /// <para>
    /// The cost is not a report that lies about mutants, it is a report that measures too few. The
    /// earlier state's traversal from the surviving suite stops at <c>Domain.Tests</c>, because that
    /// project was a suite then, so <c>Domain</c> - everything the disabled suite used to exercise -
    /// is never widened.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_suite_that_becomes_a_target_is_still_read_as_a_former_suite()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // The surviving suite already reaches the other one, which is what lets it become a target
        // the moment it stops being a suite.
        string surviving = Path.Combine(fixture.Root, "Core.Tests", "Core.Tests.csproj");

        File.WriteAllText(
            surviving,
            File.ReadAllText(surviving).Replace(
                """<ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup>""",
                """
                <ItemGroup>
                  <ProjectReference Include="../Core/Core.csproj" />
                  <ProjectReference Include="../Domain.Tests/Domain.Tests.csproj" />
                </ItemGroup>
                """,
                StringComparison.Ordinal));

        FixtureRepository.InitialiseAt(fixture.Root);

        // The change: no longer a test project, and nothing else about it moves.
        string former = Path.Combine(fixture.Root, "Domain.Tests", "Domain.Tests.csproj");

        // No line ending in any of these: the fixture is checked out with the platform's own, and a
        // search string carrying one silently matches nothing. That is what it did - on Windows the
        // OutputType survived while xUnit left, and the suite became an executable with no entry
        // point. The assertion below is here because a Replace that finds nothing says nothing.
        File.WriteAllText(
            former,
            File.ReadAllText(former)
                .Replace(
                    "<OutputType>Exe</OutputType>",
                    "<OutputType>Library</OutputType>",
                    StringComparison.Ordinal)
                .Replace(
                    """<ItemGroup><Using Include="Xunit" /></ItemGroup>""",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    """<ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.0" /></ItemGroup>""",
                    string.Empty,
                    StringComparison.Ordinal));

        string rewritten = File.ReadAllText(former);

        Assert.DoesNotContain("<OutputType>Exe</OutputType>", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("xunit", rewritten, StringComparison.OrdinalIgnoreCase);

        // Rewritten because its tests would not compile without xUnit, and a run that stops at the
        // build never reaches the selection this is about.
        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain.Tests", "BasketTests.cs"),
            """
            namespace Domain.Tests;

            public static class BasketHelp
            {
                public static int Twice(int value) => value * 2;
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        // What the disabled suite used to exercise, still a target through the surviving suite.
        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A shared build file can stop a suite being a suite, from above it.
    /// </summary>
    /// <remarks>
    /// The other half of the finding above, and it needed one more thing. A
    /// <c>tests/Directory.Build.props</c> can set a property that disables every suite beneath it -
    /// here <c>KillMutantsTestSupport</c>, which a suite does not set itself, so the file's value
    /// survives. The file is claimed for the suites beneath it long before the former-suite question
    /// is asked, and the projects it disabled are no longer among them, so it widened nothing and
    /// nothing was left unclaimed: the run returned without reading the earlier state at all.
    /// <para>
    /// The containment runs the other way here. Elsewhere a change speaks for the project whose
    /// folder holds it; a shared build file speaks for every project the folder <em>holds</em>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_build_file_that_disables_a_suite_reports_the_coverage_it_took_away()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        MoveIntoTestsDirectory(fixture, "Domain.Tests");

        string props = Path.Combine(fixture.Root, "tests", "Directory.Build.props");

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

        // The change: every project beneath tests/ declares itself test support, so Domain.Tests
        // stops being a suite without a line of its own moving. Core.Tests sits outside and keeps
        // discovery valid, which is what makes the run look healthy.
        File.WriteAllText(
            props,
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <KillMutantsTestSupport>true</KillMutantsTestSupport>
              </PropertyGroup>
            </Project>
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        // Domain was exercised by that suite and by nothing else; the run has to say so rather than
        // finish empty over the coverage the change removed.
        Assert.True(report.LostCoverage);
        Assert.Contains(
            report.CoverageLost,
            path => path.EndsWith("Domain.csproj", StringComparison.Ordinal));
    }

    /// <summary>
    /// A project the change itself declares to be test support has still lost its coverage.
    /// </summary>
    /// <remarks>
    /// The same shape as a changed <c>killmutants.json</c>: an opt-out the change being judged
    /// introduced cannot be taken at face value, or a pull request takes a project out of the targets
    /// by writing one line in its own project file and passes. What separates this from the
    /// legitimate case is where the opt-out comes from - the run's configuration, which a change
    /// cannot alter without the run refusing to judge it at all, against a project file the diff may
    /// have just written.
    /// <para>
    /// That distinction used to be drawn by subtracting the declared-support projects from what
    /// discovery left out; it is now structural, because the check asks the exclusion patterns
    /// directly and a path filter only ever answers about the run's own configuration.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_project_this_change_declares_test_support_is_still_reported_as_uncovered()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        string project = Path.Combine(fixture.Root, "Core", "Core.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "<Nullable>enable</Nullable>",
                "<Nullable>enable</Nullable>" +
                Environment.NewLine +
                "    <KillMutantsTestSupport>true</KillMutantsTestSupport>",
                StringComparison.Ordinal));

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.True(report.LostCoverage);
        Assert.Contains(
            report.CoverageLost,
            path => path.EndsWith("Core.csproj", StringComparison.Ordinal));
    }

    /// <summary>
    /// Analyzer configuration is an input like any other, because a generator can read it.
    /// </summary>
    /// <remarks>
    /// A generator reading <c>AnalyzerConfigOptionsProvider</c> emits different code for a different
    /// option, so a diff touching only an <c>.editorconfig</c> changes the assembly under test. That
    /// file sits outside every project directory, so only evaluated membership can attribute it -
    /// and measured before relying on it, <c>EditorConfigFiles</c> answers from evaluation alone and
    /// names the repository-root file.
    /// </remarks>
    [Fact]
    public async Task A_changed_editorconfig_widens_the_projects_that_read_it()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        string editorconfig = Path.Combine(fixture.Root, ".editorconfig");

        File.WriteAllText(editorconfig, $"root = true{Environment.NewLine}");

        FixtureRepository.InitialiseAt(fixture.Root);

        File.AppendAllText(
            editorconfig,
            $"{Environment.NewLine}[*.cs]{Environment.NewLine}dotnet_diagnostic.CA1000.severity = none{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Money.cs", MutatedFiles(report));
        Assert.Contains("Basket.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A suite the run excludes cannot become a former suite, whatever the change touches.
    /// </summary>
    /// <remarks>
    /// Review found this, and it is the same false positive as the excluded-project one above, one
    /// level up. Being a suite <em>now</em> is read from what discovery found, and discovery never
    /// sees an excluded project at all - so an excluded suite is always a candidate for "stopped
    /// being a suite", the earlier state agrees it was one, and it becomes a widening root. What it
    /// reached and nothing else covers is then reported as coverage this change took away, when the
    /// user's own <c>exclude</c> took it away before the diff existed.
    /// <para>
    /// The trigger is narrow, and worth stating so the test is not read as pinning more than it
    /// does: a changed <c>.cs</c> file under the excluded suite never gets here, because it is
    /// attributed as a source file and the run returns before the earlier state is read. It takes a
    /// shared build file, or a project file - something that decides what a project <em>is</em>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_excluded_suite_is_not_read_as_one_that_stopped_being_a_suite()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        // A shared build file above everything: it decides what every project is, so the run has to
        // read the earlier state rather than return.
        File.AppendAllText(
            Path.Combine(fixture.Root, "Directory.Build.props"),
            $"{Environment.NewLine}<!-- touched -->{Environment.NewLine}");

        // Domain.Tests is the only suite reaching Domain, and the run is told to leave that suite
        // alone. Domain is therefore measured by nothing - by the user's instruction, not by this
        // change - and the pattern deliberately does not cover Domain itself.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Domain.Tests/*"],
            mutators: Families,
            changes: await ChangesSinceHeadAsync(fixture.Root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.LostCoverage);
        Assert.DoesNotContain(
            report.CoverageLost,
            path => path.EndsWith("Domain.csproj", StringComparison.Ordinal));
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

    /// <summary>
    /// A file a project the run leaves out links in from elsewhere still widens through it.
    /// </summary>
    /// <remarks>
    /// Review found that a project reached but excluded was evaluated lazily, and its inputs never
    /// recorded - so a file it linked in from outside its own folder was attributed to nothing, no
    /// suite was marked touched, and the run passed over the production code that suite reaches.
    /// </remarks>
    [Fact]
    public async Task A_changed_file_an_excluded_project_links_in_widens_through_it()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // Outside every project's directory, so only evaluated membership can attribute it.
        Directory.CreateDirectory(Path.Combine(fixture.Root, "shared"));
        File.WriteAllText(
            Path.Combine(fixture.Root, "shared", "Helper.cs"),
            """
            namespace Domain;

            internal static class Helper
            {
                public static int Twice(int value) => value * 2;
            }
            """);

        Reference(
            fixture,
            "Domain",
            "Domain.csproj",
            reference: null,
            item: "<Compile Include=\"../shared/Helper.cs\" Link=\"Helper.cs\" />");

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "shared", "Helper.cs");

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Domain/*"],
            mutators: Families,
            changes: await ChangesSinceHeadAsync(fixture.Root),
            cancellationToken: TestContext.Current.CancellationToken);

        // Domain is excluded, so nothing in it is mutable and the file selects no mutants of its
        // own. What it must do is widen: Domain.Tests reaches Core through the excluded project.
        Assert.Contains("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// A reference removed by a changed production project file loses that project its coverage.
    /// </summary>
    /// <remarks>
    /// Review found the hole one layer in from the one DEC0011 argues. Removing the reference from a
    /// suite to a project is caught because the changed file belongs to a test project; removing it
    /// from a production project in the middle - <c>Tests -&gt; Domain -&gt; Core</c> becoming
    /// <c>Tests -&gt; Domain</c> - puts no test-side file in the diff at all, so nothing asked the
    /// base revision and <c>Core</c> left the targets in silence.
    /// </remarks>
    [Fact]
    public async Task A_reference_a_changed_production_project_removed_costs_that_project_its_coverage()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // Core.Tests would keep Core covered whatever Domain does, and the case is about Core
        // having no other way to be reached.
        Directory.Delete(Path.Combine(fixture.Root, "Core.Tests"), recursive: true);

        // Narrowed before the base commit, so that dropping the part of Basket that uses Core does
        // not oblige the change to touch a test file - which would mark the suite touched by the
        // ordinary rule and hide the very hole this pins. Verified: with this rewritten after the
        // commit instead, the test passed against the unfixed code.
        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain.Tests", "BasketTests.cs"),
            """
            using Domain;

            namespace Domain.Tests;

            public class BasketTests
            {
                [Fact]
                public void The_total_is_the_unit_price_times_the_quantity()
                {
                    Assert.Equal(12, Basket.Total(3, 4));
                }
            }
            """);

        FixtureRepository.InitialiseAt(fixture.Root);

        string project = Path.Combine(fixture.Root, "Domain", "Domain.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "<ProjectReference Include=\"../Core/Core.csproj\" />",
                string.Empty,
                StringComparison.Ordinal));

        // The reference goes with what used it, as it would in a real change. Two files in the
        // diff, both production: nothing here is test-side.
        File.WriteAllText(
            Path.Combine(fixture.Root, "Domain", "Basket.cs"),
            """
            namespace Domain;

            public static class Basket
            {
                public static int Total(int unitPrice, int quantity)
                {
                    return unitPrice * quantity;
                }
            }
            """);

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.True(report.LostCoverage);
        Assert.Contains(
            report.CoverageLost,
            path => path.EndsWith("Core.csproj", StringComparison.Ordinal));
    }

    /// <summary>
    /// A project outside the directory the run was pointed at never lost coverage it never had.
    /// </summary>
    /// <remarks>
    /// Review found that the coverage-loss check asked four questions and not the fifth. Point the
    /// run at one directory of a repository and a suite inside it may reference a project outside:
    /// the base graph returns it, discovery never saw it, and it read as newly uncovered - so every
    /// partial run in such a repository failed, on a fact that never changes between runs.
    /// </remarks>
    [Fact]
    public async Task A_project_outside_the_scope_of_the_run_is_not_reported_as_having_lost_coverage()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // Domain and its suite move together, so the reference between them still resolves; only
        // Domain's reference to Core now leaves the directory the run will be pointed at.
        MoveUnder(fixture, "app", "Domain");
        MoveUnder(fixture, "app", "Domain.Tests");

        string project = Path.Combine(fixture.Root, "app", "Domain", "Domain.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace("\"../Core/", "\"../../Core/", StringComparison.Ordinal));

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "app", "Domain.Tests", "BasketTests.cs");

        MutationTestReport report = await MutationTesting.RunAsync(
            Path.Combine(fixture.Root, "app"),
            mutators: Families,
            changes: await ChangesSinceHeadAsync(Path.Combine(fixture.Root, "app")),
            cancellationToken: TestContext.Current.CancellationToken);

        // The widening did happen - this is not a run that selected nothing and passed by accident.
        Assert.Contains("Basket.cs", MutatedFiles(report));
        Assert.False(report.LostCoverage);
    }

    /// <summary>
    /// A changed source generator widens the projects it generates code into.
    /// </summary>
    /// <remarks>
    /// A generator is referenced as an analyzer, so it runs at build time and nothing links it: it is
    /// neither a target nor anything a target references, and review found the consequence. Its own
    /// file is never in a consumer's compilation either — the trees it contributes carry generated
    /// paths — so the precise rule had nothing to select, and the run reported a clean pass over an
    /// assembly the change had altered.
    /// </remarks>
    [Fact]
    public async Task A_changed_source_generator_widens_the_projects_it_generates_into()
    {
        using var fixture = FixtureCopy.CreateGeneratorProject();

        // Before the base commit, and not cosmetic: this is the third test in the process to use
        // this fixture, and generators are cached by assembly identity in a load context nothing
        // unloads. Without a name of its own the run compiles against an earlier test's generator -
        // measured, and it fails the baseline with CS0103 on the very type the generator
        // contributes. RB-020.
        fixture.RenameTheGeneratorAssembly("Sample.Generator.Since");

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Sample.Generator", "LimitsGenerator.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Contains("Ages.cs", MutatedFiles(report));
    }

    /// <summary>
    /// Tracing a suite at the base revision does not widen the targets it also exercises.
    /// </summary>
    /// <remarks>
    /// The correction to a correction. Reading the base graph for a changed production project file
    /// was added so that a removed reference could not go unnoticed; review then found that the
    /// traversal widened every target it walked past, so one suite reaching independent projects had
    /// a change confined to one of them failing on an old survivor in the other. The base-side root
    /// exists to find lost coverage, and widens nothing by itself.
    /// </remarks>
    [Fact]
    public async Task Reading_the_base_graph_for_a_changed_project_does_not_widen_its_siblings()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        FixtureRepository.InitialiseAt(fixture.Root);

        // The project file alone: no source, no test, nothing else in the diff.
        File.AppendAllText(
            Path.Combine(fixture.Root, "Domain", "Domain.csproj"),
            $"{Environment.NewLine}<!-- touched -->{Environment.NewLine}");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        // Domain is widened, since its own build changed.
        Assert.Contains("Basket.cs", MutatedFiles(report));

        // Core is not. Domain.Tests reaches it too, and the base graph walks through it, but nothing
        // about this change touches what Core compiles.
        Assert.DoesNotContain("Money.cs", MutatedFiles(report));
    }

    /// <summary>
    /// Only the settings file this run reads refuses it; another one in the repository does not.
    /// </summary>
    /// <remarks>
    /// A partial run declines to judge a change to its own configuration, because `exclude` there
    /// takes effect before any selection exists. Review found the predicate matched the file name
    /// anywhere in the repository, so a monorepo measured one component at a time had every partial
    /// run refused by a change to a sibling's settings, which decide nothing here.
    /// </remarks>
    [Fact]
    public async Task A_settings_file_this_run_does_not_read_does_not_refuse_it()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // Outside every project directory, so that it widens nothing by ordinary attribution and the
        // assertion is about the refusal alone. Inside one it would widen that project, correctly:
        // a file in a project's folder is one of its inputs whatever it is called.
        Directory.CreateDirectory(Path.Combine(fixture.Root, "tools"));

        string settings = Path.Combine(fixture.Root, "tools", "killmutants.json");

        File.WriteAllText(settings, """{ "breakAt": 50 }""");

        FixtureRepository.InitialiseAt(fixture.Root);

        File.WriteAllText(settings, """{ "breakAt": 60 }""");
        Touch(fixture, "Core", "Money.cs");

        MutationTestReport report = await RunSinceHeadAsync(fixture);

        Assert.Equal(["Money.cs"], MutatedFiles(report));
    }

    /// <summary>
    /// A changed project file outside the run's scope is not read as a suite this run ever had.
    /// </summary>
    /// <remarks>
    /// The same omission as the coverage-loss check, at a second site. Candidates for "was a test
    /// project at the base revision" came from the whole repository, and a project outside the run's
    /// scope qualifies trivially — HEAD discovery, pointed elsewhere, never saw it. An unrelated
    /// change next door then widened whatever that sibling reached in scope.
    /// </remarks>
    [Fact]
    public async Task A_changed_project_outside_the_scope_of_the_run_widens_nothing_inside_it()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        MoveUnder(fixture, "app", "Domain");
        MoveUnder(fixture, "app", "Domain.Tests");

        string domain = Path.Combine(fixture.Root, "app", "Domain", "Domain.csproj");

        File.WriteAllText(
            domain,
            File.ReadAllText(domain).Replace("\"../Core/", "\"../../Core/", StringComparison.Ordinal));

        // The sibling suite reaches into the scope, which is what makes it able to widen anything.
        Reference(fixture, "Core.Tests", "Core.Tests.csproj", "../app/Domain/Domain.csproj");

        FixtureRepository.InitialiseAt(fixture.Root);
        Touch(fixture, "Core.Tests", "Core.Tests.csproj");

        MutationTestReport report = await MutationTesting.RunAsync(
            Path.Combine(fixture.Root, "app"),
            mutators: Families,
            changes: await ChangesSinceHeadAsync(Path.Combine(fixture.Root, "app")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(MutatedFiles(report));
    }

    /// <summary>
    /// A project inside a submodule is read at the base revision like any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This began as a refusal. Review found that a project inside a submodule was absent from the
    /// base state, and that the code read the absence as "there was no such project", dropping the
    /// edge that ran through it. The first answer was to detect that and stop the run.
    /// </para>
    /// <para>
    /// The fault was upstream. The base state came from <c>git archive</c>, which builds a
    /// distribution: it honours <c>export-ignore</c> and writes a submodule as an empty directory. A
    /// worktree puts the code back instead, and each submodule gets one of its own from the object
    /// store already on disk. There is nothing left to refuse - the project is simply there.
    /// </para>
    /// <para>
    /// The scenario is the one the finding described: a suite reaching into a submodule at the base
    /// revision, and the change removing that reference. What it asserts now is that the base graph
    /// could read it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_project_inside_a_submodule_is_read_at_the_base_revision()
    {
        using var outer = FixtureCopy.CreateMultiProject();
        using var inner = FixtureCopy.Create();

        FixtureRepository.InitialiseAt(inner.Root);
        FixtureRepository.InitialiseAt(outer.Root);
        FixtureRepository.AddSubmodule(outer.Root, inner.Root, "libs/Sample");

        Reference(
            outer,
            "Domain.Tests",
            "Domain.Tests.csproj",
            "../libs/Sample/Sample.Library/Sample.Library.csproj");

        FixtureRepository.CommitAll(outer.Root, "the suite reaches into the submodule");

        string project = Path.Combine(outer.Root, "Domain.Tests", "Domain.Tests.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                """<ItemGroup><ProjectReference Include="../libs/Sample/Sample.Library/Sample.Library.csproj" /></ItemGroup>""",
                string.Empty,
                StringComparison.Ordinal));

        MutationTestReport report = await RunSinceHeadAsync(outer);

        // Reached at the base revision through the reference this change removed, and measurable at
        // HEAD through the suite the submodule brings with it.
        Assert.Contains("Ages.cs", MutatedFiles(report));
    }

    private static async Task<MutationTestReport> RunSinceHeadAsync(FixtureCopy fixture) =>
        await MutationTesting.RunAsync(
            fixture.Root,
            mutators: Families,
            changes: await ChangesSinceHeadAsync(fixture.Root),
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>
    /// A git-backed change source over the fixture, which is what the CLI builds for --since.
    /// </summary>
    /// <remarks>
    /// Built here rather than named by a string, because the core no longer takes a revision: it
    /// takes something that can say what changed and hand over the code as it was. These tests
    /// exercise the git implementation of that on real repositories; the selection logic itself
    /// needs no git at all.
    /// </remarks>
    private static Task<IChangeSource> ChangesSinceHeadAsync(string directory) =>
        GitChangeSource.ForAsync(directory, "HEAD", TestContext.Current.CancellationToken);

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

    /// <summary>Moves a project one level down, under a directory of the given name.</summary>
    /// <remarks>
    /// References are left exactly as written, unlike <see cref="MoveIntoTestsDirectory"/>: this is
    /// used to move a group of projects together, where a reference inside the group still resolves
    /// and only one leaving it needs rewriting - by the test, which knows which.
    /// </remarks>
    private static void MoveUnder(FixtureCopy fixture, string directory, string project)
    {
        string from = Path.Combine(fixture.Root, project);
        string to = Path.Combine(fixture.Root, directory, project);

        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
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

    private static void Reference(
        FixtureCopy fixture,
        string directory,
        string project,
        string? reference,
        string? item = null)
    {
        string path = Path.Combine(fixture.Root, directory, project);
        string added = item ?? $"<ProjectReference Include=\"{reference}\" />";

        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(
                "</Project>",
                $"  <ItemGroup>{added}</ItemGroup>{Environment.NewLine}</Project>",
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
