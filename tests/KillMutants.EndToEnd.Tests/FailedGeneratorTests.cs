using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The quiet way a mutation testing tool can lie: reconstruct a compilation that is not the one the
/// project builds, then report confidently on it.
/// </summary>
/// <remarks>
/// Roslyn reports a generator that throws as a warning - measured: <c>CS8784</c> - drops what it
/// would have contributed, and lets the compilation emit. When the missing code is not what the
/// selected tests exercise, everything downstream looks healthy: the build works, the tests pass,
/// mutants are killed, and the score describes an assembly that does not exist.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class FailedGeneratorTests
{
    [Fact]
    public async Task A_run_stops_rather_than_reporting_on_a_compilation_a_generator_did_not_finish()
    {
        using var fixture = FixtureCopy.CreateGeneratorProject();

        fixture.AddAGeneratorThatFailsWithoutBreakingTheBuild();

        ProjectAnalysisException failure = await Assert.ThrowsAsync<ProjectAnalysisException>(
            () => MutationTesting.RunAsync(
                fixture.Root,
                mutators: [MutatorName.Create("Comparison")],
                cancellationToken: TestContext.Current.CancellationToken));

        // Asserted on our own words, not just on the diagnostic id: a fixture that failed to build
        // for some other reason would also carry a CS8784 in its output, and this test must not be
        // satisfied by that.
        Assert.StartsWith("A source generator did not run", failure.Message, StringComparison.Ordinal);

        // And it names the generator, so the reader is pointed at their code rather than at
        // KillMutants.
        Assert.Contains("CS8784", failure.Message, StringComparison.Ordinal);
        Assert.Contains("BrokenGenerator", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the same fixture with its generators working reports normally, or the check above would
    /// prove only that the fixture is hard to run.
    /// </summary>
    [Fact]
    public async Task The_same_project_with_working_generators_is_measured_as_usual()
    {
        using var fixture = FixtureCopy.CreateGeneratorProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Results);
        Assert.DoesNotContain(report.Results, result => result.Status == MutantStatus.CompileError);
    }
}
