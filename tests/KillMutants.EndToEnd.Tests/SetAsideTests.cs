using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A part of the run that cannot be measured is set aside with its reason, and the rest is measured
/// as usual (DEC0022). Before, each of these stopped the whole run: on Reefact/first-class-errors a
/// red baseline 35 minutes in took four judged projects' report with it, and a probe project outside
/// the solution stopped discovery in 15 seconds (#44).
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class SetAsideTests
{
    private static readonly MutatorName[] Families = [MutatorName.Create("Comparison")];

    /// <summary>
    /// <c>Core</c> is measured by <c>Core.Tests</c> and <c>Domain.Tests</c>; <c>Domain</c> by
    /// <c>Domain.Tests</c> alone. A test that fails in <c>Core.Tests</c> makes <c>Core</c>'s baseline
    /// red and says nothing about <c>Domain</c>'s, which is verified on its own.
    /// </summary>
    [Fact]
    public async Task A_target_with_a_red_baseline_is_set_aside_and_the_others_are_measured()
    {
        using var fixture = FixtureCopy.CreateMultiProject();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "Core.Tests", "RedOnPurpose.cs"),
            """
            namespace Core.Tests;

            public class RedOnPurpose
            {
                [Fact]
                public void Fails() => Assert.Fail("Red on purpose: Core's baseline must not pass.");
            }
            """,
            TestContext.Current.CancellationToken);

        // Comparison has sites in Core's Money and Arithmetic in Basket's `unitPrice * quantity`:
        // each target needs a mutant, or it pays no baseline at all and nothing is set aside.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison"), MutatorName.Create("Arithmetic")],
            cancellationToken: TestContext.Current.CancellationToken);

        SetAside core = Assert.Single(report.SetAside);
        Assert.Equal(SetAsideStage.Baseline, core.Stage);
        Assert.Equal("Core", core.Name);
        Assert.Contains("does not pass against unmutated code", core.Reason, StringComparison.Ordinal);
        Assert.Contains("RedOnPurpose.Fails", core.Reason, StringComparison.Ordinal);

        // Domain was measured, and only Domain.
        Assert.NotEmpty(report.Results);
        Assert.All(report.Results, result =>
            Assert.Equal("Basket.cs", Path.GetFileName(result.Mutant.Location.FilePath)));

        // A score over the part measured, which the report labels; and no gate passes.
        Assert.True(report.HasScore);
        Assert.False(report.MeasuredEverything);
    }

    /// <summary>
    /// A project MSBuild cannot read is set aside at discovery, and the rest of the directory is
    /// measured. Nothing is known of what it holds - it may have been the only suite reaching some
    /// code - so the run gives no score.
    /// </summary>
    [Fact]
    public async Task An_unreadable_project_is_set_aside_and_withholds_the_score()
    {
        using var fixture = FixtureCopy.Create();
        string probe = Path.Combine(fixture.Root, "tools", "probe");
        Directory.CreateDirectory(probe);
        await File.WriteAllTextAsync(
            Path.Combine(probe, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">", TestContext.Current.CancellationToken);

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, mutators: Families, cancellationToken: TestContext.Current.CancellationToken);

        SetAside unreadable = Assert.Single(report.SetAside);
        Assert.Equal(SetAsideStage.Discovery, unreadable.Stage);
        Assert.Equal("tools/probe/Probe.csproj", unreadable.Name);
        Assert.Contains("--exclude \"tools/probe/*\"", unreadable.Reason, StringComparison.Ordinal);

        Assert.NotEmpty(report.Results);
        Assert.False(report.HasScore);
    }

    /// <summary>
    /// When the only test project is the one that cannot be read, discovery cannot go on and there is
    /// no report to name it in: the refusal carries the reason MSBuild gave, rather than replacing it
    /// with "no test project", which would send the user looking for one that is there.
    /// </summary>
    [Fact]
    public async Task A_refusal_at_discovery_carries_what_was_set_aside_on_the_way()
    {
        using var fixture = FixtureCopy.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            TestContext.Current.CancellationToken);

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(
                fixture.Root, mutators: Families, cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith("No xUnit test project was found", refused.Message, StringComparison.Ordinal);
        Assert.Contains("1 project(s) could not be read and were set aside", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Sample.Library.Tests/Sample.Library.Tests.csproj: ", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #66, as it was met: on a fresh clone whose test projects leave their output type to a package,
    /// no test project is recognised until NuGet has restored them, and the refusal pointed at an
    /// unrelated project it had set aside. It now names the unrestored projects, by path, and the
    /// command - before what was set aside, which still follows (review of #71).
    /// </summary>
    [Fact]
    public async Task A_refusal_for_no_test_project_names_those_that_were_never_restored()
    {
        using var fixture = FixtureCopy.Create();
        fixture.LeaveTheSuitesOutputTypeToAPackage();

        string probe = Path.Combine(fixture.Root, "tools", "probe", "Probe.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        await File.WriteAllTextAsync(probe, "<Project Sdk=\"Microsoft.NET.Sdk\">", TestContext.Current.CancellationToken);

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(
                fixture.Root, mutators: Families, cancellationToken: TestContext.Current.CancellationToken));

        string message = refused.Message;
        int restore = message.IndexOf("1 project(s) reference xUnit but have not been restored", StringComparison.Ordinal);
        int setAside = message.IndexOf("1 project(s) could not be read and were set aside", StringComparison.Ordinal);

        Assert.StartsWith("No xUnit test project was found", message, StringComparison.Ordinal);
        Assert.True(restore > 0, message);
        Assert.True(setAside > restore, message);
        Assert.Contains("Run 'dotnet restore'", message, StringComparison.Ordinal);
        Assert.Contains("  Sample.Library.Tests/Sample.Library.Tests.csproj", message, StringComparison.Ordinal);
        Assert.Contains("tools/probe/Probe.csproj: ", message, StringComparison.Ordinal);
    }

    /// <summary>Once restored, the restore is not what is wrong, and the refusal does not say it is.</summary>
    [Fact]
    public async Task A_refusal_for_no_test_project_does_not_blame_a_restore_that_happened()
    {
        using var fixture = FixtureCopy.Create();
        fixture.LeaveTheSuitesOutputTypeToAPackage();
        fixture.RestoreTheSuite();

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(
                fixture.Root, mutators: Families, cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith("No xUnit test project was found", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not been restored", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Listing the tests is part of measuring coverage: a listing that fails sets the target aside
    /// at that stage, and the run reports rather than ending without a report.
    /// </summary>
    [Fact]
    public async Task A_test_listing_that_fails_sets_the_target_aside_at_coverage()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await new MutationTestSession(
                new ListingFailsRunner(new XUnitTestRunner()),
                "Release",
                workerCount: 1,
                measureCoverage: true,
                catalog: MutatorCatalog.Of(Families))
            .RunAsync(fixture.Root, TestContext.Current.CancellationToken);

        SetAside unlisted = Assert.Single(report.SetAside);
        Assert.Equal(SetAsideStage.Coverage, unlisted.Stage);
        Assert.Contains("could not list the tests", unlisted.Reason, StringComparison.Ordinal);
        Assert.Contains("timed out", unlisted.Reason, StringComparison.Ordinal);
        Assert.Empty(report.Results);
    }

    /// <summary>Runs every test as asked, and fails every listing as a listing that timed out does.</summary>
    private sealed class ListingFailsRunner(ITestRunner inner) : ITestRunner
    {
        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            throw new TestExecutionException($"Listing the tests in '{testProject.Name}' timed out.");

        public Task<TestRunOutcome> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default) =>
            inner.RunAsync(request, cancellationToken);
    }
}
