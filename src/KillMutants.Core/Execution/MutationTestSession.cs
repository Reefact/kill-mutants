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
    private readonly IChangeSource? _changes;
    private readonly IProgress<MutationTestProgress>? _progress;
    private readonly IProgress<MutantResult>? _verdicts;
    private readonly RunContinuation? _continuation;
    private readonly string? _targetFramework;

    public MutationTestSession(
        ITestRunner testRunner,
        string configuration,
        TimeoutPolicy? timeoutPolicy = null,
        int? workerCount = null,
        bool measureCoverage = true,
        IEnumerable<string>? exclude = null,
        MutatorCatalog? catalog = null,
        int verifyKills = 0,
        IChangeSource? changes = null,
        IProgress<MutationTestProgress>? progress = null,
        IProgress<MutantResult>? verdicts = null,
        RunContinuation? continuation = null,
        string? targetFramework = null)
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
        _changes = changes;
        _progress = progress;
        _verdicts = verdicts;
        _continuation = continuation;
        _targetFramework = targetFramework;
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
    /// <param name="searchDirectory">The directory to search for projects.</param>
    /// <param name="cancellationToken">Abandons the run, which then reports nothing.</param>
    /// <param name="stopToken">
    /// Ends the run early, which then reports the verdicts it had reached - see
    /// <see cref="MutationTestReport.Stopped"/>.
    /// </param>
    public async Task<MutationTestReport> RunAsync(
        string searchDirectory,
        CancellationToken cancellationToken = default,
        CancellationToken stopToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // One token for everything the run does, so a stop reaches whatever is in flight - a build, a
        // baseline, a test host - exactly as a cancellation does. What separates the two is decided
        // once, here, where the run ends: a cancelled run is abandoned, and a stopped one reports
        // what it had judged. See DEC0013.
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);

        var run = new RunSoFar(_verdicts)
        {
            Scope = _changes is null ? RunScope.WholeCodebase : RunScope.Unresolved,
        };

        // Before anything is discovered, so that a run stopped during discovery, the build or the
        // generation - which still writes a report - has a plan beside it (DEC0016). Outside the
        // handler below on purpose: a plan that cannot be written is a file problem to say now, not
        // something a stop did.
        if (_continuation is { } continuation)
        {
            run.Plan = new PlanKeeper(continuation, _workerCount);
            run.Plan.Start();
        }

        try
        {
            return await RunToTheEndAsync(searchDirectory, run, stopwatch, running.Token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (
            stopToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Whatever failed once a stop was asked for is put down to the stop, not only the
            // cancellation it raises. Stopping ends the processes the run was waiting on, and one
            // that ends that way can surface as anything its caller makes of a dead process - a red
            // baseline, an unreadable coverage file. None of it is a finding about the code.
            //
            // Stopped means a verdict is missing, and nothing less. A stop that lands while kills
            // are being re-verified can find every selected mutant already judged: re-verifying
            // adds a disagreement, never a verdict, so the population the score and the gate are
            // about is whole, and calling it incomplete would withhold both for nothing. What the
            // stop did cut short is the sample, and the report already says how many kills it
            // re-verified.
            //
            // The plan is brought up to date too, so a later run of the series knows how far this
            // one got. A write that fails here must not take the stopped report with it - this is the
            // handler, and nothing above catches what it throws - and the log already holds every
            // verdict, so what is lost is at most a disagreement.
            try
            {
                run.Plan?.Record(run.Verdicts, run.KillsReVerified);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Deliberately swallowed, for the reason above; the report that follows is complete
                // in everything but the plan's bookkeeping.
            }

            return Report(
                run,
                stopwatch.Elapsed,
                run.Verdicts.HasReachedEverySelected ? null : new EarlyStop(run.Verdicts.Selected));
        }
    }

    /// <summary>What a report is made of, gathered as the run goes so a stopped run can report it.</summary>
    private sealed class RunSoFar(IProgress<MutantResult>? verdicts)
    {
        public VerdictLedger Verdicts { get; } = new(verdicts);

        // The plan beside the report - see PlanKeeper. Null for a caller that writes no report.
        public PlanKeeper? Plan { get; set; }

        public RunScope Scope { get; set; } = RunScope.WholeCodebase;

        public ChangeSelection? Selection { get; set; }

        public IReadOnlyList<MutationTestTarget> Targets { get; set; } = [];

        // Recorded as the run goes, because a budget is derived per project and a report that omits
        // it cannot explain a timeout afterwards.
        public List<TimeBudget> Budgets { get; } = [];

        // How many kills were actually re-verified, so the report can say how strong that check was
        // rather than leaving "no disagreements" to mean either everything or nothing. Counted one
        // re-test at a time: a run stopped part way through a sample has re-tested some of it, and
        // a disagreement it found is in the report, so the count has to be there too.
        public int KillsReVerified { get; set; }
    }

    private MutationTestReport Report(RunSoFar run, TimeSpan elapsed, EarlyStop? stopped) =>
        new(
            run.Verdicts.InOrder(),
            elapsed,
            RunEnvironment.Describe(
                _workerCount, TestFrameworkOf(run.Targets), run.Budgets, run.KillsReVerified),
            run.Scope,
            run.Selection?.CoverageLost,
            run.Selection?.UnreadComponents,
            stopped,
            run.Plan?.Series(run.Verdicts),
            run.Selection?.Widened,
            run.Selection is { } selection ? selection.Touches : null);

    private async Task<MutationTestReport> RunToTheEndAsync(
        string searchDirectory,
        RunSoFar run,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // Built here rather than in the constructor: the patterns are relative to the directory the
        // run was pointed at, which only this call knows.
        PathFilter exclusions = PathFilter.Excluding(searchDirectory, _exclude);
        var discovery = new ProjectDiscovery(
            _configuration,
            exclusions,
            _progress,
            readInputFiles: _changes is not null,
            deferNoTargets: _changes is not null,
            targetFramework: _targetFramework);

        IReadOnlyList<MutationTestTarget> targets = await discovery
            .DiscoverAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        run.Targets = targets;

        // Resolved after discovery, because the selection is expressed in terms of what discovery
        // found - which projects are test projects, and which mutable projects each of them
        // exercises - and before anything is built, because a change that selects nothing must not
        // cost a build.
        ChangeSelection? selection = _changes is null
            ? null
            : await ChangeSelection
                .ResolveAsync(
                    _changes, searchDirectory, _configuration, discovery.Everything(targets),
                    exclusions, _progress, _targetFramework, cancellationToken)
                .ConfigureAwait(false);

        RunScope scope = selection?.Scope ?? RunScope.WholeCodebase;

        run.Selection = selection;
        run.Scope = scope;

        // Discovery answered with no targets rather than refusing, because a partial run has to be
        // able to ask why - and this is where the answer is. Review found the run ending before the
        // question was ever put: with one Tests -> Core pair, removing that reference empties the
        // targets, and the refusal fired before the earlier state could say that Core had just lost
        // the only suite covering it.
        //
        // Lost coverage is not the whole of the answer, and review found that too - in this guard,
        // one round later. A change can remove the reference *and* delete the project: the earlier
        // state still shows the suite reaching it, but a project that no longer exists is
        // deliberately absent from the coverage-loss report, so nothing is lost and nothing is left
        // to mutate. That is an ordinary partial run over a deletion. What separates it from the
        // misconfiguration is whether that state had production code under test at all, which is
        // the one thing the earlier traversal can say and the current state cannot.
        if (selection is not null &&
            targets.Count == 0 &&
            selection.CoverageLost.Count == 0 &&
            !selection.EarlierStateHadSomethingToMutate)
        {
            throw new ProjectAnalysisException(ProjectDiscovery.NoTargets);
        }

        if (selection is { SelectsNothing: true } || (selection is not null && targets.Count == 0))
        {
            // Nothing in the change can produce a mutant. Building the test projects and reading
            // every compilation to establish that would take a minute to reach the same empty
            // report, and a documentation-only pull request is the commonest partial run there is.
            //
            // With no targets at all there is also nothing to build, and what the report carries is
            // the coverage the change took away - which is a verdict, not an empty run.
            return new MutationTestReport(
                [], stopwatch.Elapsed, RunEnvironment.Describe(_workerCount, null, [], 0), scope,
                selection.CoverageLost, selection.UnreadComponents, widened: selection.Widened);
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
        //
        // Every project's mutants are generated before any is tested, which costs nothing - the
        // compilations are all in memory already - and lets a run that is stopped say how many it
        // had selected rather than only how many it reached.
        var generator = new MutantGenerator(_catalog, exclusions, searchDirectory);
        List<IReadOnlyList<Mutant>> mutantsOf = [];

        foreach ((MutationTestTarget target, ProjectCompilation compilation) in targets.Zip(compilations))
        {
            mutantsOf.Add(generator.Generate(
                compilation.Compilation,
                selection?.For(target.ProjectUnderTest),
                cancellationToken));
        }

        IReadOnlyList<Mutant> all = [.. mutantsOf.SelectMany(mutants => mutants)];

        run.Verdicts.Select(all);

        // The one moment the whole population is known, and so the one moment the plan can list it
        // and an earlier run's verdicts be inherited against it (DEC0015, DEC0016). Before any mutant
        // is tested, so that a run refused here has changed nothing on disk.
        run.Plan?.Begin(all, targets, scope, searchDirectory, TestFrameworkOf(targets), run.Verdicts);

        for (int index = 0; index < targets.Count; index++)
        {
            await TestTargetAsync(
                    targets[index], compilations[index], mutantsOf[index], run, cancellationToken)
                .ConfigureAwait(false);

            // After every project rather than once at the end, because the end is what a run that is
            // killed never reaches, and a plan that says how far the series got is what the next run
            // reads.
            run.Plan?.Record(run.Verdicts, run.KillsReVerified);
        }

        return Report(run, stopwatch.Elapsed, stopped: null);
    }

    private async Task TestTargetAsync(
        MutationTestTarget target,
        ProjectCompilation compilation,
        IReadOnlyList<Mutant> mutants,
        RunSoFar run,
        CancellationToken cancellationToken)
    {
        // A run that continues a series has inherited some of these already. A project with nothing
        // left pays nothing - no sandbox, no baseline, no coverage pass - which is what lets a series
        // reach the projects a single run never got to (DEC0015).
        IReadOnlyList<Mutant> remaining = [.. mutants.Where(mutant => !run.Verdicts.IsReached(mutant))];

        if (remaining.Count == 0)
        {
            return;
        }

        (Dictionary<string, string> runRoots, string[] projectRoots) = CreateRunRoots(target);
        int workerCount = Math.Min(_workerCount, remaining.Count);
        List<TestSandbox> sandboxes = [];

        try
        {
            for (int index = 0; index < workerCount; index++)
            {
                string worker = index.ToString(CultureInfo.InvariantCulture);

                sandboxes.Add(TestSandbox.CreateFor(
                    target,
                    Path.Combine(projectRoots[0], worker),
                    [.. projectRoots.Select(root => Path.Combine(root, worker))]));
            }

            TimeBudget measured = await VerifyBaselineAsync(target, compilation, sandboxes[0], cancellationToken)
                .ConfigureAwait(false);

            run.Budgets.Add(measured);

            TimeSpan budget = measured.Budget;

            // Measured over every mutant of the project, not only the remaining ones: the pass costs
            // one run per test whatever the number of sites it records, and an inherited kill that
            // --verify-kills samples below is then tested against the tests that reach it rather than
            // against the whole suite.
            CoverageMap? coverage = _measureCoverage
                ? await new CoverageCollector(_testRunner, _progress)
                    .CollectAsync(sandboxes, compilation, mutants, budget, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            MutantResult[] results = await TestMutantsAsync(
                    compilation, sandboxes, remaining, coverage, budget, run.Verdicts, cancellationToken)
                .ConfigureAwait(false);

            // Only now that every worker has stopped, so a re-run competes with nothing.
            await ConfirmTimeoutsAloneAsync(
                    compilation, sandboxes[0], remaining, results, coverage, budget, run.Verdicts,
                    cancellationToken)
                .ConfigureAwait(false);

            await ReVerifyKillsAsync(
                    compilation, sandboxes[0], mutants, coverage, budget, run, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (TestSandbox sandbox in sandboxes)
            {
                sandbox.Dispose();
            }

            foreach ((string parent, string runRoot) in runRoots)
            {
                Scratch.DeleteDirectory(runRoot);
                DeleteIfEmpty(parent);
            }
        }
    }

    /// <summary>
    /// Creates one run directory beside each test project's build output, and says which one each
    /// test project's copies go into, by its position in the target.
    /// </summary>
    /// <remarks>
    /// One per test project rather than one per target: a target can hold several suites in
    /// different directories, and a suite copied beside another suite's output loses its own
    /// ancestors - the very failure the move out of the temporary directory exists to prevent.
    /// Found in review of #58. Suites that share a parent share its run directory.
    /// </remarks>
    /// <returns>The run directories by parent, and each test project's run directory.</returns>
    internal static (Dictionary<string, string> RunRoots, string[] ProjectRoots) CreateRunRoots(MutationTestTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Dictionary<string, string> runRoots = new(ProjectPaths.Comparer);

        foreach (string parent in target.TestProjects.Select(SandboxParentOf))
        {
            if (!runRoots.ContainsKey(parent))
            {
                runRoots[parent] = Scratch.CreateDirectory(parent);
            }
        }

        return (runRoots, [.. target.TestProjects.Select(project => runRoots[SandboxParentOf(project)])]);
    }

    /// <summary>
    /// Where a test project's sandbox copies are made: beside its own build output, under
    /// <c>killmutants</c>, rather than in the temporary directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sandbox copies the test output, and the copy keeps the files but not the ancestors. A test
    /// that finds the repository by walking up from <c>AppContext.BaseDirectory</c> - a common way to
    /// reach a project file or a fixture, and the one that survives a deterministic CI build, where
    /// <c>[CallerFilePath]</c> is mapped to <c>/_/</c> - found it under <c>dotnet test</c> and found
    /// nothing from <c>/tmp</c>. On <c>Reefact/first-class-errors</c> two such tests failed the
    /// baseline of <c>RequestBinder.UnitTests</c> 35 minutes into the run, and the suite was green
    /// (#45, RB-032). Beside the build output, the copy has the ancestors the original has.
    /// </para>
    /// <para>
    /// The build output rather than the repository root, because it is the one place a project
    /// already writes to and every repository already ignores: a sandbox there leaves the working
    /// tree as clean as the build did, which a resumed series requires (DEC0015). A run that is
    /// killed now leaves its sandbox in <c>bin</c> rather than in the temporary directory (RB-006),
    /// where deleting the build output removes it.
    /// </para>
    /// </remarks>
    internal static string SandboxParentOf(TestProject testProject)
    {
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(testProject.OutputDirectory));

        return Path.Combine(Path.GetDirectoryName(output) ?? output, "killmutants");
    }

    private static void DeleteIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (IOException)
        {
            // Another target's sandboxes may share the parent; whoever empties it last removes it.
        }
        catch (UnauthorizedAccessException)
        {
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
        VerdictLedger verdicts,
        CancellationToken cancellationToken)
    {
        var pending = new ConcurrentQueue<int>(Enumerable.Range(0, mutants.Count));
        var results = new MutantResult?[mutants.Count];
        int tested = 0;

        async Task WorkAsync(TestSandbox sandbox)
        {
            while (pending.TryDequeue(out int index))
            {
                MutantResult result = await TestMutantAsync(
                        compilation, sandbox, mutants[index], coverage, budget, cancellationToken)
                    .ConfigureAwait(false);

                results[index] = result;

                // A timeout is not a verdict yet: it is re-run alone, once the workers have stopped,
                // before it is believed. Everything else is final as it lands.
                if (result.Status != MutantStatus.Timeout)
                {
                    verdicts.Reach(result);
                }

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
        VerdictLedger verdicts,
        CancellationToken cancellationToken)
    {
        int[] timedOut = [.. Enumerable
            .Range(0, results.Length)
            .Where(index => results[index].Status == MutantStatus.Timeout)];

        for (int confirmed = 0; confirmed < timedOut.Length; confirmed++)
        {
            int index = timedOut[confirmed];

            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.ConfirmingTimeouts, confirmed, timedOut.Length,
                mutants[index].Id.ToString()));

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

            verdicts.Reach(results[index]);
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
    /// <para>
    /// The sample is drawn from every kill the project has a verdict for, the ones inherited from
    /// an earlier run of the series included (DEC0015). A run that completes a series may itself
    /// have judged a handful of mutants, and a sample over those alone would leave the series'
    /// kills unchecked; a kill reached last week is as re-testable as one reached a minute ago,
    /// since the code is the same. Only a project with a remainder gets here at all, so a run that
    /// inherits everything re-verifies nothing - and its report says so, beside the series caveat.
    /// </para>
    /// </remarks>
    private async Task ReVerifyKillsAsync(
        ProjectCompilation compilation,
        TestSandbox sandbox,
        IReadOnlyList<Mutant> mutants,
        CoverageMap? coverage,
        TimeSpan budget,
        RunSoFar run,
        CancellationToken cancellationToken)
    {
        if (_verifyKills <= 0)
        {
            return;
        }

        // Spread across the run rather than taken from the front, so a sample says something about
        // the whole of it. Deterministic, so two runs of the same code sample the same mutants.
        Mutant[] killed = [.. mutants
            .Where(mutant => run.Verdicts.VerdictOf(mutant)?.Status == MutantStatus.Killed)];

        int wanted = Math.Min(_verifyKills, killed.Length);

        for (int taken = 0; taken < wanted; taken++)
        {
            Mutant mutant = killed[(int)((long)taken * killed.Length / wanted)];

            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.ReVerifyingKills, taken, wanted, mutant.Id.ToString()));

            MutantResult again = await TestMutantAsync(
                    compilation, sandbox, mutant, coverage, budget, cancellationToken)
                .ConfigureAwait(false);

            run.KillsReVerified++;

            if (again.Status != MutantStatus.Killed && run.Verdicts.VerdictOf(mutant) is { } held)
            {
                string disagreement =
                    $"reported {MutantStatus.Killed} and then {again.Status} when tested again " +
                    "on its own. One of the two runs is wrong, and this tool cannot tell which.";

                run.Verdicts.Amend(held with { Disagreement = disagreement });

                // Kept in the plan as well as in this report, so a later run of the series still
                // reports a verdict this one found not to reproduce.
                run.Plan?.Disagreed(mutant.Key, disagreement);
            }
        }
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

        for (int index = 0; index < sandbox.TestProjects.Count; index++)
        {
            TestProject testProject = sandbox.TestProjects[index];
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
                    DescribeRedBaseline(testProject, target.TestProjects[index].OutputDirectory, outcome));
            }
        }

        return _timeoutPolicy.For(total);
    }

    /// <summary>Says which tests a red baseline failed, what they said, and the two places to look.</summary>
    /// <remarks>
    /// <para>
    /// A count was all this said, with a single suggested cause - a bad rebuild. On
    /// <c>Reefact/first-class-errors</c> the suite was green under <c>dotnet test</c> and the cause
    /// was the other one: two tests found the repository by walking up from their own directory,
    /// and the sandbox had copied that directory out of it. The message xUnit wrote said so in one
    /// line, and the report kept it to itself (#46).
    /// </para>
    /// <para>
    /// So the failed tests are named with their first line, a handful of them, and both causes are
    /// given with the two directories that tell them apart. The sandbox is gone by the time this is
    /// read, but its location is the clue, not its content.
    /// </para>
    /// </remarks>
    internal static string DescribeRedBaseline(TestProject inSandbox, string originalOutput, TestRunOutcome outcome)
    {
        const int Shown = 5;
        var message = new System.Text.StringBuilder();

        message.Append(CultureInfo.InvariantCulture,
            $"'{inSandbox.Name}' does not pass against unmutated code ({outcome.WhyNotGreen()}). ");
        message.AppendLine("Mutation testing needs a green suite to mean anything.");

        foreach (TestName failed in outcome.FailedTests.Take(Shown))
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"  {failed}");

            if (outcome.FailureMessages.TryGetValue(failed, out string? said))
            {
                message.AppendLine(CultureInfo.InvariantCulture, $"    {said}");
            }
        }

        if (outcome.FailedTests.Count > Shown)
        {
            message.AppendLine(CultureInfo.InvariantCulture,
                $"  and {(outcome.FailedTests.Count - Shown).ToString(CultureInfo.InvariantCulture)} more.");
        }

        message.AppendLine("If the suite passes under 'dotnet test', one of two things differs here:");
        message.AppendLine(CultureInfo.InvariantCulture,
            $"  - where it runs: a copy of '{originalOutput}' in '{inSandbox.OutputDirectory}', which a test that finds files relative to its own location will notice;");
        message.Append("  - what it runs: KillMutants rebuilt the project from MSBuild's compiler command line, " +
                       "and the rebuild may differ from MSBuild's own.");

        return message.ToString();
    }

    private async Task<MutantResult> TestMutantAsync(
        ProjectCompilation compilation,
        TestSandbox sandbox,
        Mutant mutant,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        MutantResult result = await JudgeAsync(
                compilation, sandbox, mutant, coverage, budget, cancellationToken)
            .ConfigureAwait(false);

        // Not kept once the run is coming down, whatever it says. Stopping ends the test hosts the
        // run is waiting on, and a signal that stops this process may have reached the host too; a
        // host that ends either way reads as a crash, which counts as a kill. Such a verdict cannot
        // be told apart from one the mutation caused, so none reached at that point is kept - the
        // mutant is simply one the run did not get to.
        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }

    private async Task<MutantResult> JudgeAsync(
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
        (MutantResult? verdict, bool anyTestRan) = await RunSuitesAsync(
                sandbox, mutant, covering, budget, cancellationToken)
            .ConfigureAwait(false);

        if (verdict is not null)
        {
            return verdict;
        }

        // Survived means the tests ran and every one passed. A selection that matched no test in any
        // project ran nothing, and answering "survived" would blame the suite for a filter: the same
        // shape as RB-028, a verdict born of a run that judged nothing. The whole suite is slower and
        // never wrong, which is the trade the run already makes when coverage is unknown.
        if (!anyTestRan && covering is { Count: > 0 })
        {
            (verdict, anyTestRan) = await RunSuitesAsync(
                    sandbox, mutant, selection: null, budget, cancellationToken)
                .ConfigureAwait(false);

            if (verdict is not null)
            {
                return verdict;
            }
        }

        // Nothing ran even unfiltered. The baseline proved the unmutated suite runs tests; it did not
        // prove the mutant still produces any - cases discovered from data the mutated library
        // supplies can vanish, and a runner can report success for a host that ran nothing. Neither
        // attempt judged anything, so no verdict is recorded: "survived" would lower the score for a
        // gap in tests that were never asked, and a status of its own would widen the report's
        // format. The run stops and says which mutant, which is a broken environment and not an
        // ordinary mutation outcome.
        if (!anyTestRan)
        {
            throw new TestExecutionException(
                $"No test ran against mutant {mutant.Id} ({mutant.RelativePath}:{mutant.Location.Line}), " +
                "with the covering tests selected or with the whole suite, so there is no verdict to " +
                "report. The baseline ran tests, so the mutation changed what the test application " +
                "discovers, or the test application stopped reporting them.");
        }

        return new MutantResult(mutant, MutantStatus.Survived);
    }

    /// <summary>
    /// Runs <paramref name="selection"/> (every test when null) against each test project in turn.
    /// </summary>
    /// <returns>
    /// The verdict when a project reached one, and whether any test ran at all - which is what tells a
    /// survivor from a selection that matched nothing.
    /// </returns>
    private async Task<(MutantResult? Verdict, bool AnyTestRan)> RunSuitesAsync(
        TestSandbox sandbox,
        Mutant mutant,
        IReadOnlyList<TestName>? selection,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        bool anyTestRan = false;

        for (int index = 0; index < sandbox.TestProjects.Count; index++)
        {
            TestProject testProject = sandbox.TestProjects[index];
            TestRunOutcome outcome = await _testRunner
                .RunAsync(
                    new TestRunRequest(
                        testProject, budget, StopOnFirstFailure: true, TestNames: selection),
                    cancellationToken)
                .ConfigureAwait(false);

            if (outcome.TimedOut)
            {
                return (new MutantResult(mutant, MutantStatus.Timeout), anyTestRan);
            }

            if (outcome.Crashed)
            {
                // The baseline proved this host runs cleanly unmutated, so a crash here is
                // attributable to the mutation. The suite certainly did not pass. No test is named:
                // the host died before saying which, and inventing one would be worse than silence.
                return (new MutantResult(mutant, MutantStatus.Killed, outcome.CrashDetail), anyTestRan);
            }

            if (outcome.NoTestsRan)
            {
                // The covering tests all belong to some test project, but not necessarily this one.
                continue;
            }

            anyTestRan = true;

            if (outcome.AnyFailed)
            {
                // Named, so the kill can be reproduced without this tool: apply the mutation, run
                // these tests, watch them fail.
                return (new MutantResult(mutant, MutantStatus.Killed, KilledBy: outcome.FailedTests), true);
            }
        }

        return (null, anyTestRan);
    }

}
