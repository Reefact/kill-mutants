using System.Collections.Concurrent;
using System.Globalization;
using KillMutants.Analysis;
using KillMutants.Coverage;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
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

    public MutationTestSession(
        ITestRunner testRunner,
        string configuration,
        TimeoutPolicy? timeoutPolicy = null,
        int? workerCount = null,
        bool measureCoverage = true)
    {
        ArgumentNullException.ThrowIfNull(testRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _testRunner = testRunner;
        _configuration = configuration;
        _timeoutPolicy = timeoutPolicy ?? TimeoutPolicy.Default;
        _workerCount = workerCount ?? DefaultWorkerCount;
        _measureCoverage = measureCoverage;
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
        var discovery = new ProjectDiscovery(_configuration);

        IReadOnlyList<MutationTestTarget> targets = await discovery
            .DiscoverAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        // The real build comes first and nothing may run MSBuild after injection, so every
        // compilation is read in between. Reading one relies on the build having already produced
        // the intermediate assembly - see MsBuildQuery.GetCscCommandLineAsync.
        await discovery.BuildTestProjectsAsync(targets, cancellationToken).ConfigureAwait(false);

        List<ProjectCompilation> compilations = [];

        foreach (MutationTestTarget target in targets)
        {
            compilations.Add(await BuildCompilationAsync(target.ProjectUnderTest, cancellationToken)
                .ConfigureAwait(false));
        }

        // One generator for the whole session, so mutant identifiers never repeat across projects.
        var generator = new MutantGenerator(MutatorCatalog.Default);
        List<MutantResult> results = [];

        foreach ((MutationTestTarget target, ProjectCompilation compilation) in targets.Zip(compilations))
        {
            results.AddRange(await TestTargetAsync(target, compilation, generator, cancellationToken)
                .ConfigureAwait(false));
        }

        return new MutationTestReport(results);
    }

    private async Task<IReadOnlyList<MutantResult>> TestTargetAsync(
        MutationTestTarget target,
        ProjectCompilation compilation,
        MutantGenerator generator,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Mutant> mutants = generator.Generate(compilation.Compilation);

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

            TimeSpan budget = await VerifyBaselineAsync(target, compilation, sandboxes[0], cancellationToken)
                .ConfigureAwait(false);

            CoverageMap? coverage = _measureCoverage
                ? await new CoverageCollector(_testRunner)
                    .CollectAsync(sandboxes, compilation, mutants, budget, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            return await TestMutantsAsync(
                    compilation, sandboxes, mutants, coverage, budget, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (TestSandbox sandbox in sandboxes)
            {
                sandbox.Dispose();
            }

            DeleteQuietly(sandboxRoot);
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
    private async Task<IReadOnlyList<MutantResult>> TestMutantsAsync(
        ProjectCompilation compilation,
        IReadOnlyList<TestSandbox> sandboxes,
        IReadOnlyList<Mutant> mutants,
        CoverageMap? coverage,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var pending = new ConcurrentQueue<int>(Enumerable.Range(0, mutants.Count));
        var results = new MutantResult?[mutants.Count];

        async Task WorkAsync(TestSandbox sandbox)
        {
            while (pending.TryDequeue(out int index))
            {
                results[index] = await TestMutantAsync(
                        compilation, sandbox, mutants[index], coverage, budget, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await Task.WhenAll(sandboxes.Select(WorkAsync)).ConfigureAwait(false);

        return [.. results.Select(result => result!)];
    }

    private async Task<ProjectCompilation> BuildCompilationAsync(
        ProjectUnderTest projectUnderTest,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> commandLine = await new MsBuildQuery(_configuration)
            .GetCscCommandLineAsync(projectUnderTest.ProjectPath, cancellationToken)
            .ConfigureAwait(false);

        CSharpCommandLineArguments arguments = CscCommandLine.Parse(
            commandLine, projectUnderTest.ProjectDirectory);

        return ProjectCompilation.Create(arguments, projectUnderTest.ProjectDirectory);
    }

    /// <summary>
    /// Emits the unmutated compilation, injects it, and requires every test project to pass (ADR-0005).
    /// </summary>
    /// <remarks>
    /// This runs the baseline through exactly the path a mutant takes, which is the point:
    /// verifying the pristine build output would prove nothing about our own emit. If the
    /// reconstructed compilation is wrong in any way, every mutant would fail for that reason and
    /// be reported killed - a perfect score from a tool that is testing nothing.
    /// </remarks>
    /// <returns>The time budget to allow each mutant's test run.</returns>
    private async Task<TimeSpan> VerifyBaselineAsync(
        MutationTestTarget target,
        ProjectCompilation compilation,
        TestSandbox sandbox,
        CancellationToken cancellationToken)
    {
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
        // A null map means coverage was not measured, so every test is a candidate.
        IReadOnlyList<TestName>? covering = coverage?.TestsReaching(mutant.Id);

        if (covering is { Count: 0 })
        {
            // No test executes this code, so running the suite could only ever report the mutant as
            // survived - which would read as a gap in the tests rather than as their absence. Saying
            // NoCoverage is both true and cheaper: the suite is never run at all.
            return new MutantResult(mutant, MutantStatus.NoCoverage);
        }

        EmitOutcome emitted = compilation.EmitWith(mutant);

        if (!emitted.Success)
        {
            return new MutantResult(mutant, MutantStatus.CompileError, emitted.Diagnostics);
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
                // attributable to the mutation. The suite certainly did not pass.
                return new MutantResult(mutant, MutantStatus.Killed, outcome.CrashDetail);
            }

            if (outcome.NoTestsRan)
            {
                // The covering tests all belong to some test project, but not necessarily this one.
                continue;
            }

            if (outcome.AnyFailed)
            {
                return new MutantResult(mutant, MutantStatus.Killed);
            }
        }

        return new MutantResult(mutant, MutantStatus.Survived);
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a run over.
        }
    }

}
