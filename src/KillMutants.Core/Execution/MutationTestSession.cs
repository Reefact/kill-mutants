using System.Collections.Concurrent;
using System.Globalization;
using KillMutants.Analysis;
using KillMutants.Coverage;
using KillMutants.Filtering;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Selection;
using KillMutants.Testing;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Execution;

/// <summary>
/// Runs a whole mutation test session. The phase list is deliberately short and linear; it is the
/// one part of a mutation testing tool that must stay readable as the tool grows.
/// </summary>
internal sealed class MutationTestSession
{
    private readonly ITestRunner _testRunner;
    private readonly string _configuration;
    private readonly TimeoutPolicy _timeoutPolicy;
    private readonly int _workerCount;
    private readonly bool _measureCoverage;
    private readonly IReadOnlyList<string> _exclude;
    private readonly MutatorCatalog _catalog;
    private readonly int _verifyKills;
    private readonly string? _since;
    private readonly IProgress<MutationTestProgress>? _progress;

    public MutationTestSession(
        ITestRunner testRunner,
        string configuration,
        TimeoutPolicy? timeoutPolicy = null,
        int? workerCount = null,
        bool measureCoverage = true,
        IEnumerable<string>? exclude = null,
        MutatorCatalog? catalog = null,
        int verifyKills = 0,
        string? since = null,
        IProgress<MutationTestProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(testRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        // Refused here, not only where the command line is parsed. A caller reaching this
        // constructor directly - the library API, a test, anything that is not the CLI - used to
        // get all the way to a run with no workers at all, which builds no sandbox and then indexes
        // the first one: an IndexOutOfRangeException in place of an answer.
        if (workerCount is { } workers)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(verifyKills);

        _testRunner = testRunner;
        _configuration = configuration;
        _timeoutPolicy = timeoutPolicy ?? TimeoutPolicy.Default;
        _workerCount = workerCount ?? DefaultWorkerCount;
        _measureCoverage = measureCoverage;
        _exclude = [.. exclude ?? []];
        _catalog = catalog ?? MutatorCatalog.Default;
        _verifyKills = verifyKills;
        _since = since;
        _progress = progress;
    }

    /// <summary>
    /// How many mutants are tested at once by default.
    /// </summary>
    /// <remarks>
    /// Half the logical processors, because each worker starts a test host that runs the suite's own
    /// tests in parallel too. Claiming every core for workers would oversubscribe the machine and
    /// make each run slower, which also inflates the timeout budget derived from the baseline.
    /// </remarks>
    private static int DefaultWorkerCount => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Discovers, mutates, tests and reports.</summary>
    public async Task<MutationTestReport> RunAsync(
        string searchDirectory,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Built here rather than in the constructor: the patterns are relative to the directory the
        // run was pointed at, which only this call knows.
        PathFilter exclusions = PathFilter.Excluding(searchDirectory, _exclude);
        var discovery = new ProjectDiscovery(
            _configuration, exclusions, _progress, readInputFiles: _since is not null);

        IReadOnlyList<MutationTestTarget> targets = await discovery
            .DiscoverAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        // Resolved after discovery, because the selection is expressed in terms of what discovery
        // found - which projects are test projects, and which mutable projects each of them
        // exercises - and before anything is built, because a change that selects nothing must not
        // cost a build.
        ChangeSelection? selection = _since is null
            ? null
            : await ChangeSelection
                .ResolveAsync(
                    _since, searchDirectory, _configuration, discovery.Everything(targets),
                    _progress, cancellationToken)
                .ConfigureAwait(false);

        RunScope scope = selection?.Scope ?? RunScope.WholeCodebase;

        if (selection is { SelectsNothing: true })
        {
            // Nothing in the change can produce a mutant. Building the test projects and reading
            // every compilation to establish that would take a minute to reach the same empty
            // report, and a documentation-only pull request is the commonest partial run there is.
            return new MutationTestReport(
                [], stopwatch.Elapsed, RunEnvironment.Describe(_workerCount, null, [], 0), scope,
                selection.CoverageLost);
        }

        // The real build comes first and nothing may run MSBuild after injection, so every
        // compilation is read in between. Reading one relies on the build having already produced
        // the intermediate assembly - see MsBuildQuery.GetCscCommandLineAsync.
        await discovery.BuildTestProjectsAsync(targets, cancellationToken).ConfigureAwait(false);

        List<ProjectCompilation> compilations = [];

        foreach (MutationTestTarget target in targets)
        {
            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.Analysing, compilations.Count, targets.Count,
                target.ProjectUnderTest.Name));

            compilations.Add(await BuildCompilationAsync(target.ProjectUnderTest, cancellationToken)
                .ConfigureAwait(false));
        }

        // One generator for the whole session, so mutant identifiers never repeat across projects.
        var generator = new MutantGenerator(_catalog, exclusions, searchDirectory);
        List<MutantResult> results = [];

        // Recorded as the run goes, because a budget is derived per project and a report that omits
        // it cannot explain a timeout afterwards.
        List<TimeBudget> budgets = [];

        // How many kills were actually re-verified, so the report can say how strong that check was
        // rather than leaving "no disagreements" to mean either everything or nothing.
        List<int> verified = [];

        foreach ((MutationTestTarget target, ProjectCompilation compilation) in targets.Zip(compilations))
        {
            results.AddRange(await TestTargetAsync(
                    target, compilation, generator, selection, budgets, verified, cancellationToken)
                .ConfigureAwait(false));
        }

        return new MutationTestReport(
            results,
            stopwatch.Elapsed,
            RunEnvironment.Describe(
                _workerCount, TestFrameworkOf(targets), budgets, verified.Sum()),
            scope,
            selection?.CoverageLost);
    }

    private async Task<IReadOnlyList<MutantResult>> TestTargetAsync(
        MutationTestTarget target,
        ProjectCompilation compilation,
        MutantGenerator generator,
        ChangeSelection? selection,
        List<TimeBudget> budgets,
        List<int> verified,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Mutant> mutants = generator.Generate(
            compilation.Compilation, selection?.For(target.ProjectUnderTest));

        if (mutants.Count == 0)
        {
            return [];
        }

        string sandboxRoot = Path.Combine(Path.GetTempPath(), $"killmutants-{Guid.NewGuid():N}");
        int workerCount = Math.Min(_workerCount, mutants.Count);
        List<TestSandbox> sandboxes = [];

        try
        {
            for (int index = 0; index < workerCount; index++)
            {
                sandboxes.Add(TestSandbox.CreateFor(
                    target,
                    Path.Combine(sandboxRoot, index.ToString(CultureInfo.InvariantCulture))));
            }

            TimeBudget measured = await VerifyBaselineAsync(target, compilation, sandboxes[0], cancellationToken)
                .ConfigureAwait(false);

            budgets.Add(measured);

            TimeSpan budget = measured.Budget;

            CoverageMap? coverage = _measureCoverage
                ? await new CoverageCollector(_testRunner, _progress)
                    .CollectAsync(sandboxes, compilation, mutants, budget, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            MutantResult[] results = await TestMutantsAsync(
                    compilation, sandboxes, mutants, coverage, budget, cancellationToken)
                .ConfigureAwait(false);

            // Only now that every worker has stopped, so a re-run competes with nothing.
            await ConfirmTimeoutsAloneAsync(
                    compilation, sandboxes[0], mutants, results, coverage, budget, cancellationToken)
                .ConfigureAwait(false);

            verified.Add(await ReVerifyKillsAsync(
                    compilation, sandboxes[0], mutants, results, coverage, budget, cancellationToken)
                .ConfigureAwait(false));

            return results;
        }
        finally
        {
            foreach (TestSandbox sandbox in sandboxes)
            {
                sandbox.Dispose();
            }

            Scratch.DeleteDirectory(sandboxRoot);
        }
    }

    /// <summary>
    /// Tests every mutant, one per worker at a time, each worker in its own sandbox.
    /// </summary>
    /// <remarks>
    /// Workers pull from a shared queue rather than taking a fixed share, because mutants are not
    /// equally expensive: one that times out costs the whole budget while its neighbours finish in
    /// milliseconds. Results are re-ordered afterwards so the report does not depend on which worker
    /// happened to finish first.
    /// </remarks>
    private async Task<MutantResult[]> TestMutantsAsync(
        ProjectCompilation compilation,
        IReadOnlyList<TestSandbox> sandboxes,
        IReadOnlyList<Mutant> mutants,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var pending = new ConcurrentQueue<int>(Enumerable.Range(0, mutants.Count));
        var results = new MutantResult?[mutants.Count];
        int tested = 0;

        async Task WorkAsync(TestSandbox sandbox)
        {
            while (pending.TryDequeue(out int index))
            {
                results[index] = await TestMutantAsync(
                        compilation, sandbox, mutants[index], coverage, budget, cancellationToken)
                    .ConfigureAwait(false);

                _progress?.Report(new MutationTestProgress(
                    MutationTestPhase.TestingMutants, Interlocked.Increment(ref tested), mutants.Count));
            }
        }

        await Task.WhenAll(sandboxes.Select(WorkAsync)).ConfigureAwait(false);

        return [.. results.Select(result => result!)];
    }

    /// <summary>
    /// Re-tests every mutant that timed out, alone, and keeps whatever verdict that produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The budget is derived from a baseline measured with nothing else running, and then spent by
    /// workers competing with each other for the machine. A healthy but slow mutant can exceed it
    /// for that reason alone - and a timeout counts as a <em>detection</em>, so the mistake inflates
    /// the score rather than depressing it. That is the worst direction for an error to go in:
    /// a suite is credited with catching something it never noticed.
    /// </para>
    /// <para>
    /// Widening the budget or scaling it by the worker count would make this less likely without
    /// making it impossible, and would slow every genuine endless loop down in exchange. Running the
    /// timeouts again once the workers have finished removes the cause instead: at that point
    /// nothing else of ours is running, so a mutant that still exceeds its budget is slow on its own
    /// merits. The cost is one extra run per timeout, and timeouts are rare - on a suite where they
    /// are not, they are the mutants that already dominate the run.
    /// </para>
    /// <para>
    /// Measured on a four-core machine, four concurrent runs of a start-up-dominated suite cost 18%
    /// more than one alone, which the default budget absorbs easily. A CPU-bound suite has no such
    /// bound, which is why this is a rule rather than a wider margin.
    /// </para>
    /// </remarks>
    private async Task ConfirmTimeoutsAloneAsync(
        ProjectCompilation compilation,
        TestSandbox sandbox,
        IReadOnlyList<Mutant> mutants,
        MutantResult[] results,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        int[] timedOut = [.. Enumerable
            .Range(0, results.Length)
            .Where(index => results[index].Status == MutantStatus.Timeout)];

        foreach (int index in timedOut)
        {
            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.ConfirmingTimeouts, 0, timedOut.Length, mutants[index].Id.ToString()));

            MutantResult alone = await TestMutantAsync(
                    compilation, sandbox, mutants[index], coverage, budget, cancellationToken)
                .ConfigureAwait(false);

            // The second verdict wins - that is the whole point of taking it. But a timeout that
            // does not survive being re-run alone is evidence about the budget, and replacing the
            // status without saying so leaves the run silently better than it was measured. A tool
            // that corrects itself has to be able to say how often it had to.
            results[index] = alone.Status == MutantStatus.Timeout
                ? alone
                : alone with
                {
                    Overturned =
                        $"timed out with {_workerCount.ToString(CultureInfo.InvariantCulture)} " +
                        $"worker(s) running, then {alone.Status} when re-run on its own. The budget " +
                        "was exceeded because of the load, not because of the mutation.",
                };
        }
    }

    /// <summary>
    /// Tests a sample of the mutants reported killed a second time, and disagrees loudly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this file guards against a mutant being wrongly reported as <em>alive</em>.
    /// This guards the other direction, which is the worse one: a mutant wrongly reported as killed
    /// is a gap in the tests that gets celebrated instead of fixed. The baseline is verified once, at
    /// the start; after that any failing test counts as a kill, whatever made it fail. A test that is
    /// flaky, order-dependent or sensitive to the machine produces exactly that.
    /// </para>
    /// <para>
    /// The check is a re-run, not an argument: same mutant, same tests, on a machine with nothing
    /// else of ours running. A verdict that does not survive its own repetition was never a
    /// measurement. It is a sample because certainty here costs a second full run, and a sample is
    /// what turns "our kills are sound" from a belief into something a CI job checks every time.
    /// </para>
    /// <para>
    /// Off unless asked, because it costs one test run per sampled mutant and that is the user's
    /// time. What it never does is change a verdict: a disagreement is reported, not silently
    /// resolved, because which of the two runs told the truth is not ours to decide.
    /// </para>
    /// </remarks>
    private async Task<int> ReVerifyKillsAsync(
        ProjectCompilation compilation,
        TestSandbox sandbox,
        IReadOnlyList<Mutant> mutants,
        MutantResult[] results,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        if (_verifyKills <= 0)
        {
            return 0;
        }

        // Spread across the run rather than taken from the front, so a sample says something about
        // the whole of it. Deterministic, so two runs of one commit sample the same mutants.
        int[] killed = [.. Enumerable
            .Range(0, results.Length)
            .Where(index => results[index].Status == MutantStatus.Killed)];

        int wanted = Math.Min(_verifyKills, killed.Length);

        for (int taken = 0; taken < wanted; taken++)
        {
            int index = killed[(int)((long)taken * killed.Length / wanted)];

            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.ReVerifyingKills, taken, wanted, mutants[index].Id.ToString()));

            MutantResult again = await TestMutantAsync(
                    compilation, sandbox, mutants[index], coverage, budget, cancellationToken)
                .ConfigureAwait(false);

            if (again.Status != MutantStatus.Killed)
            {
                results[index] = results[index] with
                {
                    Disagreement =
                        $"reported {MutantStatus.Killed} and then {again.Status} when tested again " +
                        "on its own. One of the two runs is wrong, and this tool cannot tell which.",
                };
            }
        }

        return wanted;
    }

    /// <summary>The xUnit the test applications will run on, read from what was built.</summary>
    /// <remarks>
    /// Already known: discovery refuses anything but xUnit 4, and reads the version from the
    /// assembly in the output directory to do it. Reporting it costs nothing and makes two runs
    /// comparable - a runner and a laptop resolving different SDKs is exactly the difference nobody
    /// notices until it has cost a day.
    /// </remarks>
    private static string? TestFrameworkOf(IEnumerable<MutationTestTarget> targets)
    {
        string[] versions =
        [
            .. targets
                .SelectMany(target => target.TestProjects)
                .Select(test => XUnitVersion.In(Path.GetDirectoryName(test.AssemblyPath)!))
                .Where(version => version is not null)
                .Select(version => $"xUnit {version!.ToString(3)}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        return versions.Length == 0 ? null : string.Join(", ", versions);
    }

    private async Task<ProjectCompilation> BuildCompilationAsync(
        ProjectUnderTest projectUnderTest,
        CancellationToken cancellationToken)
    {
        // The framework matters: a project targeting several compiles only in its inner builds, and
        // an outer-build query answers with an empty command line rather than an error.
        IReadOnlyList<string> commandLine = await new MsBuildQuery(_configuration)
            .GetCscCommandLineAsync(
                projectUnderTest.ProjectPath, projectUnderTest.TargetFramework, cancellationToken)
            .ConfigureAwait(false);

        CSharpCommandLineArguments arguments = CscCommandLine.Parse(
            commandLine, projectUnderTest.ProjectDirectory);

        return ProjectCompilation.Create(arguments, projectUnderTest.ProjectDirectory);
    }

    /// <summary>
    /// Emits the unmutated compilation, injects it, and requires every test project to pass (DEC0005).
    /// </summary>
    /// <remarks>
    /// This runs the baseline through exactly the path a mutant takes, which is the point:
    /// verifying the pristine build output would prove nothing about our own emit. If the
    /// reconstructed compilation is wrong in any way, every mutant would fail for that reason and
    /// be reported killed - a perfect score from a tool that is testing nothing.
    /// </remarks>
    /// <returns>The time budget to allow each mutant's test run, and what it was derived from.</returns>
    private async Task<TimeBudget> VerifyBaselineAsync(
        MutationTestTarget target,
        ProjectCompilation compilation,
        TestSandbox sandbox,
        CancellationToken cancellationToken)
    {
        _progress?.Report(new MutationTestProgress(
            MutationTestPhase.VerifyingBaseline, Subject: target.ProjectUnderTest.Name));

        EmitOutcome baseline = compilation.EmitBaseline();

        if (!baseline.Success)
        {
            throw new BaselineVerificationException(
                $"KillMutants could not compile '{target.ProjectUnderTest.Name}' from the compiler " +
                $"command line MSBuild reported.{Environment.NewLine}{baseline.Diagnostics}");
        }

        sandbox.Inject(baseline.Assembly!);

        var total = TimeSpan.Zero;

        foreach (TestProject testProject in sandbox.TestProjects)
        {
            TestRunOutcome outcome = await _testRunner
                .RunAsync(new TestRunRequest(testProject, TimeSpan.FromMinutes(10)), cancellationToken)
                .ConfigureAwait(false);

            total += outcome.Duration;

            if (outcome.Crashed)
            {
                throw new BaselineVerificationException(
                    $"The test application for '{testProject.Name}' could not be run against " +
                    $"unmutated code.{Environment.NewLine}{outcome.CrashDetail}");
            }

            if (outcome.NoTestsRan)
            {
                throw new BaselineVerificationException(
                    $"'{testProject.Name}' ran no tests, so no mutant could ever be killed.");
            }

            if (!outcome.AllPassed)
            {
                throw new BaselineVerificationException(
                    $"'{testProject.Name}' does not pass against unmutated code " +
                    $"({outcome.Failed.ToString(CultureInfo.InvariantCulture)} failed). " +
                    "Mutation testing needs a green suite to mean anything. " +
                    "This may also indicate that KillMutants rebuilt the project incorrectly.");
            }
        }

        return _timeoutPolicy.For(total);
    }

    private async Task<MutantResult> TestMutantAsync(
        ProjectCompilation compilation,
        TestSandbox sandbox,
        Mutant mutant,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // Null twice over means the same thing and is treated the same way: coverage was not
        // measured at all, or this site could carry no recorder. Either way every test is a
        // candidate, which is slower but never wrong.
        IReadOnlyList<TestName>? covering = coverage?.TestsReaching(mutant.Id);

        // The emit comes first, even when nothing covers the mutant. A mutation that cannot be built
        // is untestable and belongs outside the score; calling it NoCoverage instead counts it as
        // undetected and holds the score down with a mutant no suite could ever have judged. The
        // saving that mattered is untouched - the test suite is still never run for it.
        EmitOutcome emitted = compilation.EmitWith(mutant);

        if (!emitted.Success)
        {
            return new MutantResult(mutant, MutantStatus.CompileError, emitted.Diagnostics);
        }

        if (covering is { Count: 0 })
        {
            // No test executes this code, so running the suite could only ever report the mutant as
            // survived - which would read as a gap in the tests rather than as their absence.
            return new MutantResult(mutant, MutantStatus.NoCoverage);
        }

        sandbox.Inject(emitted.Assembly!);

        // A mutant is killed by the first suite that notices it; the rest would add nothing.
        foreach (TestProject testProject in sandbox.TestProjects)
        {
            TestRunOutcome outcome = await _testRunner
                .RunAsync(
                    new TestRunRequest(
                        testProject, budget, StopOnFirstFailure: true, TestNames: covering),
                    cancellationToken)
                .ConfigureAwait(false);

            if (outcome.TimedOut)
            {
                return new MutantResult(mutant, MutantStatus.Timeout);
            }

            if (outcome.Crashed)
            {
                // The baseline proved this host runs cleanly unmutated, so a crash here is
                // attributable to the mutation. The suite certainly did not pass. No test is named:
                // the host died before saying which, and inventing one would be worse than silence.
                return new MutantResult(mutant, MutantStatus.Killed, outcome.CrashDetail);
            }

            if (outcome.NoTestsRan)
            {
                // The covering tests all belong to some test project, but not necessarily this one.
                continue;
            }

            if (outcome.AnyFailed)
            {
                // Named, so the kill can be reproduced without this tool: apply the mutation, run
                // these tests, watch them fail.
                return new MutantResult(
                    mutant, MutantStatus.Killed, KilledBy: outcome.FailedTests);
            }
        }

        return new MutantResult(mutant, MutantStatus.Survived);
    }

}
