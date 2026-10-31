using KillMutants.Git;
using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A partial run compares the suites' reach now with their reach before the change, and the earlier
/// state has to be read for the framework the suites run on. See DEC0021.
/// </summary>
/// <remarks>
/// Evaluated without a framework, the outer build of a multi-targeted project has <c>TargetFramework</c>
/// empty, and every item conditioned on it is absent. A reference written
/// <c>Condition="'$(TargetFramework)' == 'net10.0'"</c> is then not in the earlier graph at all, so a
/// change that removes it leaves nothing to compare: the library has lost its only suite and the run
/// passes. A wrong green, which is why this is pinned rather than left as a known gap.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class TargetFrameworkSinceTests
{
    private static readonly MutatorName[] Families = [MutatorName.Create("Comparison")];

    [Fact]
    public async Task A_reference_removed_under_a_framework_condition_is_still_seen_to_be_lost()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedSuiteProject();
        fixture.ReferenceTheLibraryOnlyOnNet10();

        FixtureRepository.InitialiseAt(fixture.Root);

        fixture.DropTheLibraryFromTheSuite();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: Families,
            changes: await GitChangeSource.ForAsync(fixture.Root, "HEAD", TestContext.Current.CancellationToken),
            targetFramework: "net10.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.LostCoverage);
        Assert.Contains(
            report.CoverageLost,
            path => path.EndsWith("Sample.Library.csproj", StringComparison.Ordinal));
    }
}
