using KillMutants.Execution;
using KillMutants.Reporting;
using KillMutants.Testing.XUnit;

namespace KillMutants;

/// <summary>Runs mutation testing over a directory containing an xUnit 4 test project.</summary>
public static class MutationTesting
{
    /// <summary>
    /// The mutator families this tool knows, which are the names <c>mutators</c> accepts.
    /// </summary>
    /// <remarks>
    /// The catalog itself stays internal - it is a set of rules, not an extension point - but its
    /// names are part of the command line and so of the public surface.
    /// </remarks>
    public static IReadOnlyList<Mutations.MutatorName> MutatorFamilies =>
        Mutations.Mutators.MutatorCatalog.Names;

    /// <summary>
    /// Discovers the test project and the project it exercises, mutates the latter, and reports
    /// which mutations the tests caught.
    /// </summary>
    /// <param name="searchDirectory">The directory to search for projects.</param>
    /// <param name="configuration">The build configuration to analyse and run.</param>
    /// <param name="workerCount">
    /// How many mutants to test at once. Defaults to half the logical processors, because each
    /// worker starts a test host that runs the suite's own tests in parallel too.
    /// </param>
    /// <param name="measureCoverage">
    /// Measure which tests reach which mutants, and run only those. Turning this off runs the whole
    /// suite for every mutant, which is slower but needs no instrumented build.
    /// </param>
    /// <param name="exclude">
    /// Patterns for projects and source files to leave alone, relative to
    /// <paramref name="searchDirectory"/> and written with <c>/</c>. An excluded project is neither
    /// mutated nor used to run tests; an excluded file is still compiled but never mutated.
    /// </param>
    /// <param name="mutators">
    /// The only mutator families to run, or null for all of them. Names come from
    /// <see cref="Mutations.Mutators.MutatorCatalog.Names"/>.
    /// </param>
    /// <param name="withoutMutators">Families to leave out, applied after <paramref name="mutators"/>.</param>
    /// <param name="progress">Told where the run has got to, so a caller can show it.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <exception cref="ArgumentException">A named mutator family does not exist.</exception>
    /// <exception cref="Projects.ProjectAnalysisException">The projects could not be analysed.</exception>
    /// <exception cref="BaselineVerificationException">The unmutated code does not pass its tests.</exception>
    /// <exception cref="Testing.TestExecutionException">The test application could not be run.</exception>
    /// <exception cref="Coverage.CoverageException">
    /// Coverage could not be measured, so no run was attempted rather than one measured from a build
    /// that could not be trusted. <paramref name="measureCoverage"/> turns the measurement off.
    /// </exception>
    public static Task<MutationTestReport> RunAsync(
        string searchDirectory,
        string configuration = "Release",
        int? workerCount = null,
        bool measureCoverage = true,
        IEnumerable<string>? exclude = null,
        IEnumerable<Mutations.MutatorName>? mutators = null,
        IEnumerable<Mutations.MutatorName>? withoutMutators = null,
        IProgress<Reporting.MutationTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new MutationTestSession(
            new XUnitTestRunner(), configuration, timeoutPolicy: null, workerCount, measureCoverage,
            exclude, Mutations.Mutators.MutatorCatalog.Of(mutators, withoutMutators), progress);

        return session.RunAsync(searchDirectory, cancellationToken);
    }
}
