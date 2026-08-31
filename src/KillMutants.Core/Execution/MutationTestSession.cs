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

    public MutationTestSession(ITestRunner testRunner, string configuration)
    {
        ArgumentNullException.ThrowIfNull(testRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _testRunner = testRunner;
        _configuration = configuration;
    }

    /// <summary>Discovers, mutates, tests and reports.</summary>
    public async Task<MutationTestReport> RunAsync(
        string searchDirectory,
        CancellationToken cancellationToken = default)
    {
        MutationTestTarget target = await new ProjectDiscovery(_configuration)
            .DiscoverAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        ProjectCompilation compilation = await BuildCompilationAsync(target.ProjectUnderTest, cancellationToken)
            .ConfigureAwait(false);

        using var injection = AssemblyInjection.Protect(target.InjectionPath);

        TimeSpan mutantBudget = await VerifyBaselineAsync(target, compilation, injection, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default)
            .Generate(compilation.Compilation);

        List<MutantResult> results = [];

        foreach (Mutant mutant in mutants)
        {
            results.Add(await TestMutantAsync(target, compilation, injection, mutant, mutantBudget, cancellationToken)
                .ConfigureAwait(false));
        }

        return new MutationTestReport(results);
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
    /// Emits the unmutated compilation, injects it, and requires the tests to pass (ADR-0005).
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

        TestRunOutcome outcome = await _testRunner
            .RunAsync(target.TestProject, TimeSpan.FromMinutes(10), stopOnFirstFailure: false, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Crashed)
        {
            throw new BaselineVerificationException(
                $"The test application for '{target.TestProject.Name}' could not be run against " +
                $"unmutated code.{Environment.NewLine}{outcome.CrashDetail}");
        }

        if (outcome.NoTestsRan)
        {
            throw new BaselineVerificationException(
                $"'{target.TestProject.Name}' ran no tests, so no mutant could ever be killed.");
        }

        if (!outcome.AllPassed)
        {
            throw new BaselineVerificationException(
                $"'{target.TestProject.Name}' does not pass against unmutated code " +
                $"({outcome.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture)} failed). " +
                "Mutation testing needs a green suite to mean anything. " +
                "This may also indicate that KillMutants rebuilt the project incorrectly.");
        }

        return BudgetFor(outcome.Duration);
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

        TestRunOutcome outcome = await _testRunner
            .RunAsync(target.TestProject, budget, stopOnFirstFailure: true, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.TimedOut)
        {
            return new MutantResult(mutant, MutantStatus.Timeout);
        }

        if (outcome.Crashed)
        {
            // The baseline proved this host runs cleanly unmutated, so a crash here is attributable
            // to the mutation. The suite certainly did not pass, which is what killing a mutant means.
            return new MutantResult(mutant, MutantStatus.Killed, outcome.CrashDetail);
        }

        if (outcome.NoTestsRan)
        {
            throw new TestExecutionException(
                $"The test application ran no tests against mutant {mutant.Id}, " +
                "so its outcome cannot be trusted.");
        }

        return new MutantResult(mutant, outcome.AnyFailed ? MutantStatus.Killed : MutantStatus.Survived);
    }

    /// <summary>
    /// Allows a mutant three times the baseline duration plus a fixed margin. A mutation can turn a
    /// terminating loop into an endless one, so a mutant that never finishes must be recorded as
    /// timed out rather than allowed to hang the run.
    /// </summary>
    private static TimeSpan BudgetFor(TimeSpan baseline) =>
        TimeSpan.FromSeconds((baseline.TotalSeconds * 3) + 30);
}
