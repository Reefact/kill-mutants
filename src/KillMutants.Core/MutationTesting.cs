using KillMutants.Execution;
using KillMutants.Reporting;
using KillMutants.Testing.XUnit;

namespace KillMutants;

/// <summary>Runs mutation testing over a directory containing an xUnit 4 test project.</summary>
public static class MutationTesting
{
    /// <summary>
    /// The mutator families this tool knows, which are the names <c>mutators</c> accepts - the opt-in
    /// ones included, see <see cref="OptInMutatorFamilies"/>.
    /// </summary>
    /// <remarks>
    /// The catalog itself stays internal - it is a set of rules, not an extension point - but its
    /// names are part of the command line and so of the public surface.
    /// </remarks>
    public static IReadOnlyList<Mutations.MutatorName> MutatorFamilies =>
        Mutations.Mutators.MutatorCatalog.Names;

    /// <summary>
    /// The families a run applies only when it names them, in <c>mutators</c> or
    /// <c>withMutators</c>. DEC0019.
    /// </summary>
    public static IReadOnlyList<Mutations.MutatorName> OptInMutatorFamilies =>
        Mutations.Mutators.MutatorCatalog.OptInNames;

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
    /// The only mutator families to run, or null for the default catalog - every family except the
    /// opt-in ones. Names come from <see cref="MutatorFamilies"/>.
    /// </param>
    /// <param name="withoutMutators">
    /// Families to leave out, applied after <paramref name="mutators"/> and
    /// <paramref name="withMutators"/>.
    /// </param>
    /// <param name="withMutators">
    /// Families to run on top of <paramref name="mutators"/>, which is how an opt-in family joins the
    /// default catalog.
    /// </param>
    /// <param name="verifyKills">
    /// How many of the mutants reported killed to test a second time, on their own, before the run
    /// ends. Zero, the default, skips the check; each sampled mutant costs one more test run.
    /// </param>
    /// <param name="changes">
    /// Where to learn what changed and what the code was before it, or null to mutate the whole
    /// codebase. Such a run reports findings and a binary verdict rather than a mutation score: see
    /// DEC0010, and <see cref="Reporting.MutationTestReport.Scope"/>. The caller supplies one; this
    /// assembly provides none, and nothing here depends on where an implementation comes from or on
    /// what it reads to answer.
    /// </param>
    /// <param name="progress">Told where the run has got to, so a caller can show it.</param>
    /// <param name="verdicts">
    /// Told each verdict the moment it is final, so a caller can keep it somewhere a run that never
    /// finishes cannot take with it - see <see cref="Reporting.VerdictLog"/>. A timeout is told once
    /// it has been confirmed alone; a disagreement found by <paramref name="verifyKills"/> is in the
    /// report only.
    /// </param>
    /// <param name="continuation">
    /// The series this run belongs to and the plan it keeps beside its report, or null for a run that
    /// keeps none - see <see cref="Execution.RunContinuation"/>, DEC0015 and DEC0016.
    /// </param>
    /// <param name="cancellationToken">Cancels the run, which then reports nothing.</param>
    /// <param name="stopToken">
    /// Stops the run early. It ends what is in flight, as cancelling does, and then returns a report
    /// of the verdicts reached so far, marked as stopped - see
    /// <see cref="Reporting.MutationTestReport.Stopped"/> and DEC0013.
    /// </param>
    /// <param name="targetFramework">
    /// The one framework the test projects run on, or null when none was named. Needed for a test
    /// project that targets several; a project that does not offer it stops the run. See DEC0021.
    /// </param>
    /// <param name="ignoreCalls">
    /// The calls the project does not hear about: a mutant of such a call's statement or arguments is
    /// generated, reported as ignored and never tested. See DEC0026.
    /// </param>
    /// <exception cref="ArgumentException">A named mutator family does not exist.</exception>
    /// <exception cref="Projects.ProjectAnalysisException">The projects could not be analysed.</exception>
    /// <exception cref="BaselineVerificationException">The unmutated code does not pass its tests.</exception>
    /// <exception cref="Testing.TestExecutionException">The test application could not be run.</exception>
    /// <exception cref="Coverage.CoverageException">
    /// Coverage could not be measured, so no run was attempted rather than one measured from a build
    /// that could not be trusted. <paramref name="measureCoverage"/> turns the measurement off.
    /// </exception>
    /// <exception cref="Selection.ChangeSelectionException">
    /// <paramref name="changes"/> was given and the change, or the project graph of the code before
    /// it, could not be read. The run stops rather than falling back to what the current state alone
    /// can say.
    /// </exception>
    /// <exception cref="Execution.ResumeException">
    /// <paramref name="continuation"/> continues a series this run cannot continue honestly: the
    /// mutants generated, the project graph or the test framework differ from the plan's.
    /// </exception>
    public static Task<MutationTestReport> RunAsync(
        string searchDirectory,
        string configuration = "Release",
        int? workerCount = null,
        bool measureCoverage = true,
        IEnumerable<string>? exclude = null,
        IEnumerable<Mutations.MutatorName>? mutators = null,
        IEnumerable<Mutations.MutatorName>? withoutMutators = null,
        IEnumerable<Mutations.MutatorName>? withMutators = null,
        int verifyKills = 0,
        Selection.IChangeSource? changes = null,
        IProgress<Reporting.MutationTestProgress>? progress = null,
        IProgress<Reporting.MutantResult>? verdicts = null,
        Execution.RunContinuation? continuation = null,
        string? targetFramework = null,
        IEnumerable<string>? ignoreCalls = null,
        CancellationToken cancellationToken = default,
        CancellationToken stopToken = default)
    {
        var session = new MutationTestSession(
            new XUnitTestRunner(), configuration, timeoutPolicy: null, workerCount, measureCoverage,
            exclude, Mutations.Mutators.MutatorCatalog.Of(mutators, withoutMutators, withMutators), verifyKills,
            changes, progress, verdicts, continuation, targetFramework, Filtering.IgnoredCalls.Of(ignoreCalls));

        return session.RunAsync(searchDirectory, cancellationToken, stopToken);
    }
}
