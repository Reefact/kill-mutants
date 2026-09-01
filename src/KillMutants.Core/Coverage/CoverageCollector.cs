using System.Collections.Concurrent;
using KillMutants.Analysis;
using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;

namespace KillMutants.Coverage;

/// <summary>
/// Works out which tests reach which mutants, by running each test on its own against a build whose
/// mutation sites record having been reached.
/// </summary>
/// <remarks>
/// <para>
/// One run per test rather than one run for the whole suite, because the question is not "is this
/// code reached" but "reached <em>by which test</em>". Running them one at a time is what makes the
/// attribution exact, with no cross-test interference to reason about.
/// </para>
/// <para>
/// The cost is one process launch per test, paid once. It buys skipping every uncovered mutant
/// entirely, and running only a handful of tests for each of the rest - against a mutant count that
/// is normally an order of magnitude larger than the test count.
/// </para>
/// </remarks>
internal sealed class CoverageCollector
{
    private readonly ITestRunner _testRunner;
    private readonly IProgress<MutationTestProgress>? _progress;

    public CoverageCollector(ITestRunner testRunner, IProgress<MutationTestProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(testRunner);

        _testRunner = testRunner;
        _progress = progress;
    }

    /// <summary>Measures what each test reaches.</summary>
    /// <exception cref="CoverageException">The instrumented build could not be produced.</exception>
    public async Task<CoverageMap> CollectAsync(
        IReadOnlyList<TestSandbox> sandboxes,
        ProjectCompilation compilation,
        IReadOnlyList<Mutant> mutants,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sandboxes);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(mutants);

        MutationSites sites = MutationSites.From(mutants, compilation.Compilation);
        EmitOutcome instrumented = compilation.EmitInstrumented(sites);

        if (!instrumented.Success)
        {
            throw new CoverageException(
                "KillMutants could not build an instrumented copy of the project, so it cannot tell " +
                "which tests reach which mutants." + Environment.NewLine + instrumented.Diagnostics);
        }

        foreach (TestSandbox sandbox in sandboxes)
        {
            sandbox.Inject(instrumented.Assembly!);
        }

        await VerifyInstrumentedBuildAsync(sandboxes[0], cancellationToken).ConfigureAwait(false);

        IReadOnlyList<(TestProject Project, TestName Test)> tests =
            await DiscoverAsync(sandboxes[0], cancellationToken).ConfigureAwait(false);

        var pending = new ConcurrentQueue<(TestProject Project, TestName Test)>(tests);
        var observed = new ConcurrentBag<CoverageObservation>();
        int measured = 0;

        async Task WorkAsync(TestSandbox sandbox)
        {
            while (pending.TryDequeue(out (TestProject Project, TestName Test) work))
            {
                observed.Add(new CoverageObservation(
                    work.Test,
                    await MeasureAsync(sandbox, work.Project, work.Test, budget, cancellationToken)
                        .ConfigureAwait(false)));

                _progress?.Report(new MutationTestProgress(
                    MutationTestPhase.MeasuringCoverage, Interlocked.Increment(ref measured), tests.Count));
            }
        }

        await Task.WhenAll(sandboxes.Select(WorkAsync)).ConfigureAwait(false);

        return CoverageMap.From(observed, sites);
    }

    /// <summary>
    /// Runs the whole suite once against the instrumented build and requires it green.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The baseline check proves our reconstructed compilation behaves like the real one. It says
    /// nothing about the <em>second</em> transformation coverage applies, which wraps every mutation
    /// site in a call. If that changed behaviour, every measurement after it would still look
    /// perfectly valid: a coverage map built from a program that no longer does what it did is
    /// worse than no map, because nothing downstream can tell.
    /// </para>
    /// <para>
    /// One suite run answers it, which is the cheapest honest check available - the per-test phase
    /// that follows costs one run per test. It also earns the right to treat a single test failing
    /// during that phase as an isolation-dependent test rather than as broken instrumentation.
    /// </para>
    /// </remarks>
    /// <exception cref="CoverageException">The instrumented build does not behave like the baseline.</exception>
    private async Task VerifyInstrumentedBuildAsync(
        TestSandbox sandbox,
        CancellationToken cancellationToken)
    {
        foreach (TestProject testProject in sandbox.TestProjects)
        {
            TestRunOutcome outcome = await _testRunner
                .RunAsync(new TestRunRequest(testProject, TimeSpan.FromMinutes(10)), cancellationToken)
                .ConfigureAwait(false);

            if (outcome.WhyNotGreen() is { } reason)
            {
                throw new CoverageException(
                    $"'{testProject.Name}' passes against unmutated code but not against the build " +
                    $"KillMutants instrumented to measure coverage: {reason}. The instrumentation " +
                    "changed the program's behaviour, so any coverage measured from it would be " +
                    "untrustworthy. Re-run with --no-coverage to test every mutant against the whole " +
                    "suite instead.");
            }
        }
    }

    private async Task<IReadOnlyList<(TestProject, TestName)>> DiscoverAsync(
        TestSandbox sandbox,
        CancellationToken cancellationToken)
    {
        List<(TestProject, TestName)> tests = [];

        foreach (TestProject testProject in sandbox.TestProjects)
        {
            foreach (TestName test in await _testRunner
                         .DiscoverAsync(testProject, cancellationToken).ConfigureAwait(false))
            {
                tests.Add((testProject, test));
            }
        }

        return tests;
    }

    /// <summary>
    /// Runs one test and reads back the mutation sites it reached, or null when that could not be
    /// established.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of this method. "This test reaches nothing" and "we could
    /// not find out what this test reaches" look identical downstream unless they are kept apart
    /// here, and collapsing them turns a failed measurement into a mutant that is never run and is
    /// then reported as undetected - a verdict nothing ever tested.
    /// </remarks>
    private async Task<IReadOnlyList<MutantId>?> MeasureAsync(
        TestSandbox sandbox,
        TestProject testProject,
        TestName test,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // The sandbox is private to this worker, so the recorder's output file can be too.
        string outputPath = Path.Combine(sandbox.Root, $"coverage-{Guid.NewGuid():N}.txt");

        // The project has to be rebased onto this worker's sandbox, since discovery ran against
        // another one.
        var rebased = sandbox.TestProjects.FirstOrDefault(
            candidate => candidate.ProjectPath == testProject.ProjectPath) ?? testProject;

        try
        {
            TestRunOutcome outcome = await _testRunner.RunAsync(
                    new TestRunRequest(
                        rebased,
                        budget,
                        StopOnFirstFailure: false,
                        TestNames: [test],
                        Environment: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [CoverageProbe.OutputPathVariable] = outputPath,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            // Every one of these means the measurement did not happen, not that nothing was
            // reached. A timeout or crash stops the recorder mid-run; a filter that matched nothing
            // never ran the test at all; and a failing test stops early, so its hits are a prefix of
            // what it would have reached - using them would under-report just as badly.
            return outcome.WhyNotGreen() is null ? CoverageFile.Read(outputPath) : null;
        }
        finally
        {
            Scratch.DeleteFile(outputPath);
        }
    }

}
