using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// DEC0026, through a real run: a mutant of a call the project does not hear about is generated,
/// never tested, reported as ignored outside the score and counted by rule - a rule that silenced
/// nothing included.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class IgnoredCallsRunTests
{
    [Fact]
    public async Task A_silenced_call_is_reported_as_ignored_and_never_tested()
    {
        using var fixture = FixtureCopy.Create();

        // Statement removes `register.Add(age);`, the fixture's one call statement, and Comparison
        // gives the run mutants that are heard, so the score covers those and not the silenced one.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Statement"), MutatorName.Create("Comparison")],
            ignoreCalls: ["*.Add", "*.Nobody"],
            cancellationToken: TestContext.Current.CancellationToken);

        MutantResult silenced = Assert.Single(report.Results, result => result.Status == MutantStatus.Ignored);

        Assert.Equal("Statement", silenced.Mutant.Mutator.ToString());
        Assert.Equal("register.Add(age);", silenced.Mutant.OriginalText);
        Assert.Equal(["*.Add"], silenced.Mutant.IgnoredBy);
        Assert.Empty(silenced.KilledBy);

        Assert.Equal(1, report.Ignored);
        Assert.Equal(
            [new IgnoredRule("*.Add", 1), new IgnoredRule("*.Nobody", 0)],
            report.IgnoredByRule);

        // Heard mutants were judged and define the score; the silenced one is outside it.
        Assert.True(report.Detected > 0);
        Assert.Equal(report.Detected + report.Undetected + report.Ignored, report.Total);
        Assert.True(report.IsComplete);
        Assert.True(report.HasScore);
    }
}
