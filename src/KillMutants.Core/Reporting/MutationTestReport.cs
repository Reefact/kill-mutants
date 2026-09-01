using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>The outcome of a whole mutation test run.</summary>
public sealed class MutationTestReport
{
    /// <summary>Creates a report from the results of every mutant tested.</summary>
    /// <param name="results">Every mutant, with its outcome.</param>
    /// <param name="duration">How long the whole run took.</param>
    /// <param name="environment">What the run ran on, and under what limits.</param>
    public MutationTestReport(
        IReadOnlyList<MutantResult> results,
        TimeSpan duration = default,
        RunEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        Results = results;
        Duration = duration;
        Environment = environment;
        Killed = Count(MutantStatus.Killed);
        Survived = Count(MutantStatus.Survived);
        TimedOut = Count(MutantStatus.Timeout);
        CompileErrors = Count(MutantStatus.CompileError);
        Uncovered = Count(MutantStatus.NoCoverage);
        Detected = CountOutcome(MutantOutcome.Detected);
        Undetected = CountOutcome(MutantOutcome.Undetected);
        Untestable = CountOutcome(MutantOutcome.Untestable);
        Score = MutationScore.FromCounts(Detected, Undetected);

        // Ordered by what each family cost, because that is the order a reader acts on: the
        // expensive families with little to show are the ones worth reconsidering.
        ByMutator =
        [
            .. results
                .GroupBy(result => result.Mutant.Mutator)
                .Select(family => new MutatorSummary(
                    family.Key,
                    family.Count(),
                    family.Count(result => result.Outcome == MutantOutcome.Detected),
                    family.Count(result => result.Outcome == MutantOutcome.Undetected),
                    family.Count(result => result.Outcome == MutantOutcome.Untestable)))
                .OrderByDescending(family => family.Total)
                .ThenBy(family => family.Mutator.ToString(), StringComparer.Ordinal),
        ];

        Warnings = RunWarning.For(this);

        int Count(MutantStatus status) => results.Count(result => result.Status == status);
        int CountOutcome(MutantOutcome outcome) => results.Count(result => result.Outcome == outcome);
    }

    /// <summary>Every mutant, with its outcome.</summary>
    public IReadOnlyList<MutantResult> Results { get; }

    /// <summary>How long the whole run took.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// What the run ran on and under what limits, or null when it was not recorded.
    /// </summary>
    /// <remarks>
    /// Two reports without this are not comparable, and a mutant reported as timed out cannot be
    /// explained after the fact without the budget it exceeded. See <see cref="RunEnvironment"/>.
    /// </remarks>
    public RunEnvironment? Environment { get; }

    /// <summary>How many mutants were generated.</summary>
    public int Total => Results.Count;

    /// <summary>Mutants the tests caught.</summary>
    public int Killed { get; }

    /// <summary>Mutants the tests missed.</summary>
    public int Survived { get; }

    /// <summary>Mutants whose test run exceeded its time budget.</summary>
    public int TimedOut { get; }

    /// <summary>Mutants that did not compile and so could not be tested.</summary>
    public int CompileErrors { get; }

    /// <summary>Mutants in code no test reaches, which were therefore never run.</summary>
    public int Uncovered { get; }

    /// <summary>Mutants the suite noticed: <see cref="Killed"/> plus <see cref="TimedOut"/>.</summary>
    public int Detected { get; }

    /// <summary>
    /// Mutants the suite did not notice: <see cref="Survived"/> plus <see cref="Uncovered"/>.
    /// </summary>
    public int Undetected { get; }

    /// <summary>
    /// Mutants the suite was never asked about, because KillMutants could not build them.
    /// </summary>
    public int Untestable { get; }

    /// <summary>The share of judged mutants the suite detected.</summary>
    public MutationScore Score { get; }

    /// <summary>What each mutator family cost and bought, most mutants first.</summary>
    public IReadOnlyList<MutatorSummary> ByMutator { get; }

    /// <summary>What a reader must know before trusting the score - see <see cref="RunWarning"/>.</summary>
    public IReadOnlyList<RunWarning> Warnings { get; }
}
