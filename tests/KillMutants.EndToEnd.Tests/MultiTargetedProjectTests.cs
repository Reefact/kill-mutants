using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A library built for several frameworks is ordinary, and until this test nothing here had ever
/// met one.
/// </summary>
/// <remarks>
/// <para>
/// Discovery already resolves such a project's facts against the framework its test project loads.
/// The compiler command line did not: it asked MSBuild about the project without naming a
/// framework, and MSBuild answers that question on the OUTER build, which compiles nothing.
/// Measured against the .NET 10 SDK, <c>-getItem:CscCommandLineArgs</c> then returns an empty list
/// - not an error, not a partial answer, an empty one.
/// </para>
/// <para>
/// The empty command line is caught one layer down, so this fails rather than lying - but it fails
/// on a project that is perfectly well formed, blaming a build that did happen. A library targeting
/// two frameworks is ordinary C#, and KillMutants could not mutate one at all.
/// </para>
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class MultiTargetedProjectTests
{
    [Fact]
    public async Task A_library_built_for_several_frameworks_is_mutated_for_the_one_its_tests_load()
    {
        using var fixture = FixtureCopy.CreateMultiTargetedProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, cancellationToken: TestContext.Current.CancellationToken);

        // The reconstruction has to have found the real sources: no mutant at all is exactly what an
        // empty command line produces, so an empty report would pass a weaker assertion.
        MutantResult mutant = Assert.Single(
            report.Results, result => result.Mutant.MutatedText == "age > 18");

        Assert.Equal(MutantStatus.Killed, mutant.Status);
        Assert.All(report.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
    }
}
