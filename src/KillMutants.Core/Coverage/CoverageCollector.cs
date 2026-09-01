using System.Collections.Concurrent;
using System.Globalization;
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

        IReadOnlyList<(TestProject Project, TestName Test)> tests =
            await DiscoverAsync(sandboxes[0], cancellationToken).ConfigureAwait(false);

        var pending = new ConcurrentQueue<(TestProject Project, TestName Test)>(tests);
        var observed = new ConcurrentBag<(TestName, IReadOnlyList<MutantId>)>();
        int measured = 0;

        async Task WorkAsync(TestSandbox sandbox)
        {
            while (pending.TryDequeue(out (TestProject Project, TestName Test) work))
            {
                observed.Add((
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

    /// <summary>Runs one test and reads back the mutation sites it reached.</summary>
    private async Task<IReadOnlyList<MutantId>> MeasureAsync(
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

            // A test that times out or crashes tells us nothing reliable about what it reached.
            // Claiming it reaches nothing would wrongly mark mutants as uncovered, so it is left out
            // of the map and its mutants keep whatever coverage other tests give them.
            return outcome.TimedOut || outcome.Crashed ? [] : Read(outputPath);
        }
        finally
        {
            DeleteQuietly(outputPath);
        }
    }

    private static List<MutantId> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<MutantId> reached = [];

        foreach (string line in File.ReadAllLines(path))
        {
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                reached.Add(MutantId.FromValue(value));
            }
        }

        return reached;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a run over.
        }
    }
}
