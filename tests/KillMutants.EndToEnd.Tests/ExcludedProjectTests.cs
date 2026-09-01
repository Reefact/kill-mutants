using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Excluding a project means "do not mutate this", never "pretend this does not exist".
/// </summary>
/// <remarks>
/// The difference only shows on a graph deeper than two: with <c>Domain.Tests -> Domain -> Core</c>
/// and <c>Domain</c> excluded, discovery used to stop at the excluded project and never reach
/// <c>Core</c>. Nothing said so. The run reported on whatever it did find, and a component that the
/// test suite genuinely exercises was simply absent from the score.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class ExcludedProjectTests
{
    [Fact]
    public async Task A_project_behind_an_excluded_one_is_still_mutated()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        // Core.Tests is excluded as well, or Core would be reached directly and the traversal
        // through Domain - the thing under test - would never be needed.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Core.Tests/*", "Domain/*"],
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        string[] mutated = [.. report.Results
            .Select(result => Path.GetFileName(result.Mutant.Location.FilePath))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["Money.cs"], mutated);
    }

    /// <summary>
    /// And the excluded project itself is still left alone, which is the other half of the contract.
    /// </summary>
    [Fact]
    public async Task An_excluded_project_is_not_mutated()
    {
        using var fixture = FixtureCopy.CreateMultiProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            exclude: ["Domain/*"],
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            report.Results,
            result => Path.GetFileName(result.Mutant.Location.FilePath) == "Basket.cs");

        Assert.Contains(
            report.Results,
            result => Path.GetFileName(result.Mutant.Location.FilePath) == "Money.cs");
    }
}
