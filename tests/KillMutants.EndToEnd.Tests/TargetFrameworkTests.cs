using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A test project on several frameworks is ordinary, and a run could neither choose between them nor
/// be told to. <c>--target-framework</c> names the one the test projects run on; without it an
/// ambiguous run is refused, and the refusal says what to type. See DEC0021.
/// </summary>
/// <remarks>
/// A score describes one build of the code, so a default would produce one for a framework nobody
/// chose. And a test project that does not offer the chosen framework stops the run rather than being
/// set aside: the code that only it tested would leave the report without the score showing it.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class TargetFrameworkTests
{
    [Fact]
    public async Task A_test_project_on_several_frameworks_is_refused_with_the_option_that_lifts_it()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedSuiteProject();

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(fixture.Root, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("--target-framework", refused.Message, StringComparison.Ordinal);
        Assert.Contains("net8.0, net10.0", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_named_framework_is_the_one_the_test_project_runs_on()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedSuiteProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, targetFramework: "net10.0", cancellationToken: TestContext.Current.CancellationToken);

        MutantResult mutant = Assert.Single(
            report.Results, result => result.Mutant.MutatedText == "age > 18");

        Assert.Equal(MutantStatus.Killed, mutant.Status);
    }

    [Fact]
    public async Task A_framework_no_test_project_offers_stops_the_run_and_names_the_project()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedSuiteProject();

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(
                fixture.Root, targetFramework: "net9.0", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Sample.Library.Tests", refused.Message, StringComparison.Ordinal);
        Assert.Contains("net8.0, net10.0", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_test_project_on_another_single_framework_stops_the_run_rather_than_vanishing()
    {
        using var fixture = FixtureCopy.CreateNetStandardProject();

        ProjectAnalysisException refused = await Assert.ThrowsAsync<ProjectAnalysisException>(() =>
            MutationTesting.RunAsync(
                fixture.Root, targetFramework: "net8.0", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Sample.Library.Tests", refused.Message, StringComparison.Ordinal);
        Assert.Contains("net10.0", refused.Message, StringComparison.Ordinal);
        Assert.Contains("--exclude", refused.Message, StringComparison.Ordinal);
    }
}
