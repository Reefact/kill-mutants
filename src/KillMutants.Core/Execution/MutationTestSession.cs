using System.Globalization;
using KillMutants.Analysis;
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

    public MutationTestSession(
        ITestRunner testRunner,
        string configuration,
        TimeoutPolicy? timeoutPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(testRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _testRunner = testRunner;
        _configuration = configuration;
        _timeoutPolicy = timeoutPolicy ?? TimeoutPolicy.Default;
    }

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
        using var injection = AssemblyInjection.Protect(target.InjectionPaths);

        TimeSpan mutantBudget = await VerifyBaselineAsync(target, compilation, injection, cancellationToken)
            .ConfigureAwait(false);

        List<MutantResult> results = [];

        foreach (Mutant mutant in generator.Generate(compilation.Compilation))
        {
            results.Add(await TestMutantAsync(target, compilation, injection, mutant, mutantBudget, cancellationToken)
                .ConfigureAwait(false));
        }

        return results;
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
        AssemblyInjection injection,
        CancellationToken cancellationToken)
    {
        EmitOutcome baseline = compilation.EmitBaseline();

        if (!baseline.Success)
        {
            throw new BaselineVerificationException(
                $"KillMutants could not compile '{target.ProjectUnderTest.Name}' from the compiler " +
                $"command line MSBuild reported.{Environment.NewLine}{baseline.Diagnostics}");
        }

        injection.Inject(baseline.Assembly!);

        var total = TimeSpan.Zero;

        foreach (TestProject testProject in target.TestProjects)
        {
            TestRunOutcome outcome = await _testRunner
                .RunAsync(testProject, TimeSpan.FromMinutes(10), stopOnFirstFailure: false, cancellationToken)
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
        MutationTestTarget target,
        ProjectCompilation compilation,
        AssemblyInjection injection,
        Mutant mutant,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        EmitOutcome emitted = compilation.EmitWith(mutant);

        if (!emitted.Success)
        {
            return new MutantResult(mutant, MutantStatus.CompileError, emitted.Diagnostics);
        }

        injection.Inject(emitted.Assembly!);

        // A mutant is killed by the first suite that notices it; the rest would add nothing.
        foreach (TestProject testProject in target.TestProjects)
        {
            TestRunOutcome outcome = await _testRunner
                .RunAsync(testProject, budget, stopOnFirstFailure: true, cancellationToken)
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
                throw new TestExecutionException(
                    $"'{testProject.Name}' ran no tests against mutant {mutant.Id}, " +
                    "so its outcome cannot be trusted.");
            }

            if (outcome.AnyFailed)
            {
                return new MutantResult(mutant, MutantStatus.Killed);
            }
        }

        return new MutantResult(mutant, MutantStatus.Survived);
    }
}
