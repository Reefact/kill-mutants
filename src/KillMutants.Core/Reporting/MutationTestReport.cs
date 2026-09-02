using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>The outcome of a whole mutation test run.</summary>
public sealed class MutationTestReport
{
    /// <summary>Creates a report from the results of every mutant tested.</summary>
    /// <param name="results">Every mutant, with its outcome.</param>
    /// <param name="duration">How long the whole run took.</param>
    /// <param name="environment">What the run ran on, and under what limits.</param>
    /// <param name="scope">What population was inspected. Defaults to the whole codebase.</param>
    public MutationTestReport(
        IReadOnlyList<MutantResult> results,
        TimeSpan duration = default,
        RunEnvironment? environment = null,
        RunScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        Results = results;
        Duration = duration;
        Environment = environment;
        Scope = scope ?? RunScope.WholeCodebase;
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
    /// <remarks>
    /// Computed whatever the scope, and <em>reported</em> only for a full run. A partial run's
    /// population is defined against a base revision chosen per run, so a percentage over it answers
    /// "how well did the suite do on this change?" and no question at all that spans two runs.
    /// DEC0010 decides that such a number is not printed; it is not hidden from a caller holding
    /// this object, which knows from <see cref="Scope"/> what it is looking at.
    /// </remarks>
    public MutationScore Score { get; }

    /// <summary>What population this run inspected - see <see cref="RunScope"/>.</summary>
    public RunScope Scope { get; }

    /// <summary>
    /// True when the run answered "did the selected scope produce an undetected mutant?" with yes.
    /// </summary>
    /// <remarks>
    /// The gate a partial run offers in place of a threshold, per DEC0010. Both undetected statuses
    /// count: a change that adds code nothing tests at all produces <c>NoCoverage</c> and not
    /// <c>Survived</c>, and a gate reading only survivors would wave through the clearest case of
    /// newly introduced untested behaviour there is.
    /// </remarks>
    public bool HasUndetected => Undetected > 0;

    /// <summary>
    /// True when mutants were generated and not one of them could be tested.
    /// </summary>
    /// <remarks>
    /// Not the same as an empty run, and the difference is the whole point. A change with no mutants
    /// has nothing to answer for and passes; a change whose mutants all failed to compile
    /// established nothing, and a run that established nothing must not report success - the same
    /// rule ADR-0009 already applies to an undefined score against a threshold.
    /// </remarks>
    public bool IsInconclusive => Total > 0 && Score.IsUndefined;

    /// <summary>What each mutator family cost and bought, most mutants first.</summary>
    public IReadOnlyList<MutatorSummary> ByMutator { get; }

    /// <summary>What a reader must know before trusting the score - see <see cref="RunWarning"/>.</summary>
    public IReadOnlyList<RunWarning> Warnings { get; }
}
