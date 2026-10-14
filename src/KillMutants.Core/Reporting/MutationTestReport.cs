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
    /// <param name="coverageLost">
    /// Projects the change stopped covering entirely, which have no mutants here because nothing
    /// reaches them any more.
    /// </param>
    /// <param name="unreadComponents">
    /// Components the earlier state was reconstructed without, which make the comparison behind
    /// every other figure here provisional.
    /// </param>
    /// <param name="stopped">
    /// How far the run had got when it was stopped, or null when it ran to the end.
    /// </param>
    /// <param name="series">
    /// The series this run belongs to and what it inherited from it, or null for a run that named none.
    /// </param>
    /// <param name="widened">
    /// The projects a partial run mutated whole rather than file by file, and why.
    /// </param>
    /// <param name="inTheChange">
    /// Whether the change touched a source file itself, asked of each undetected mutant when a
    /// project was widened. Null when nothing was.
    /// </param>
    public MutationTestReport(
        IReadOnlyList<MutantResult> results,
        TimeSpan duration = default,
        RunEnvironment? environment = null,
        RunScope? scope = null,
        IReadOnlyList<string>? coverageLost = null,
        IReadOnlyList<string>? unreadComponents = null,
        EarlyStop? stopped = null,
        RunSeries? series = null,
        IReadOnlyList<WidenedProject>? widened = null,
        Func<string, bool>? inTheChange = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        Results = results;
        Duration = duration;
        Environment = environment;
        Scope = scope ?? RunScope.WholeCodebase;
        CoverageLost = coverageLost ?? [];
        UnreadComponents = unreadComponents ?? [];
        Stopped = stopped;
        Series = series;
        Widened = widened ?? [];
        Killed = Count(MutantStatus.Killed);
        Survived = Count(MutantStatus.Survived);
        TimedOut = Count(MutantStatus.Timeout);
        CompileErrors = Count(MutantStatus.CompileError);
        Uncovered = Count(MutantStatus.NoCoverage);
        Detected = CountOutcome(MutantOutcome.Detected);
        Undetected = CountOutcome(MutantOutcome.Undetected);
        Untestable = CountOutcome(MutantOutcome.Untestable);
        Score = MutationScore.FromCounts(Detected, Undetected);
        UndetectedFromWidening = Widened.Count == 0 || inTheChange is null
            ? 0
            : results.Count(result =>
                result.Outcome == MutantOutcome.Undetected &&
                !inTheChange(result.Mutant.Location.FilePath));

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
    /// population is defined against an earlier state chosen per run, so a percentage over it answers
    /// "how well did the suite do on this change?" and no question at all that spans two runs.
    /// DEC0010 decides that such a number is not printed; it is not hidden from a caller holding
    /// this object, which knows from <see cref="Scope"/> what it is looking at.
    /// </remarks>
    public MutationScore Score { get; }

    /// <summary>What population this run inspected - see <see cref="RunScope"/>.</summary>
    public RunScope Scope { get; }

    /// <summary>
    /// Projects the change stopped covering: exercised in the earlier state, still there, and
    /// reached by no test project any more.
    /// </summary>
    /// <remarks>
    /// Not mutants, and that is the point. Nothing reaches these projects, so no suite could judge a
    /// mutant in one and the run has none to report - which is exactly how a change that deletes a
    /// component's last test came to read as a clean pass. They are named instead, and a partial run
    /// that has any does not pass.
    /// </remarks>
    public IReadOnlyList<string> CoverageLost { get; }

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

    /// <summary>True when the change left a project with no tests reaching it at all.</summary>
    public bool LostCoverage => CoverageLost.Count > 0;

    /// <summary>
    /// The projects a partial run mutated whole rather than file by file, each with the changes that
    /// made it do so. Empty for a full run, and for a partial run that stayed precise.
    /// </summary>
    /// <remarks>
    /// DEC0011 widens on purpose, and a run that did is a different run from one that did not: a
    /// change to one existing test can put a whole solution in scope. Before this was reported, the
    /// only scope line described the diff, and a run over most of a codebase announced itself as a
    /// run over eight files (#22).
    /// </remarks>
    public IReadOnlyList<WidenedProject> Widened { get; }

    /// <summary>
    /// Undetected mutants in files the change did not touch, in the run only because a project was
    /// widened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number a reviewer wants first and the one the verdict cannot give: on a widened run, a red
    /// gate may be reporting the codebase's existing gaps rather than anything the change did. These
    /// mutants still count, in <see cref="Undetected"/> and in the verdict - DEC0011 widens because a
    /// change to a test can stop a mutant being killed without touching the file it sits in - so
    /// this splits the finding and does not discount it.
    /// </para>
    /// <para>
    /// A file, not a line. A mutant in a changed file on a line the change did not write is counted
    /// as in the change, because the selection itself knows no finer unit than the file.
    /// </para>
    /// </remarks>
    public int UndetectedFromWidening { get; }

    /// <summary>The components the comparison could not read, empty when it was complete.</summary>
    public IReadOnlyList<string> UnreadComponents { get; }

    /// <summary>True when the earlier state was reconstructed without part of the code.</summary>
    /// <remarks>
    /// <para>
    /// A third reason to withhold a pass, beside lost coverage and an inconclusive selection, and
    /// the same rule underneath all three: a run that established nothing must not report success.
    /// Here what was not established is the comparison itself.
    /// </para>
    /// <para>
    /// It cannot be narrowed to "the missing component mattered", and the measurement says why: an
    /// import from a component that is not there is skipped by MSBuild in silence - the evaluation
    /// succeeds and simply answers with fewer references - so a project outside the missing path
    /// loses edges without any trace to detect. Absence of evidence is exactly what is on offer.
    /// </para>
    /// <para>
    /// The run still happens and the report still says everything it found: refusing outright would
    /// throw away a report worth reading, and passing with a footnote would be a green a gate acts
    /// on. Abstaining costs only the green.
    /// </para>
    /// </remarks>
    public bool ComparisonIsIncomplete => UnreadComponents.Count > 0;

    /// <summary>How far the run had got when it was stopped, or null when it ran to the end.</summary>
    /// <remarks>
    /// <para>
    /// A stopped run holds the verdicts it had reached and no others, so every count here is a count
    /// of what it judged. Those are facts, and they are kept. What they cannot be is a description of
    /// the codebase: the mutants judged are the ones the run reached first - whole projects in the
    /// order they were found, then the front of the next - and not a sample, so a score over them
    /// would describe the files that happened to come first. See DEC0013.
    /// </para>
    /// <para>
    /// A mutant that timed out and had not yet been re-run on its own when the run stopped has no
    /// verdict, and is not among <see cref="Results"/>: a timeout is believed only once it has been
    /// confirmed alone, and counting an unconfirmed one would credit the suite with a detection the
    /// run had not established.
    /// </para>
    /// </remarks>
    public EarlyStop? Stopped { get; }

    /// <summary>True when every mutant the run selected has a verdict.</summary>
    public bool IsComplete => Stopped is null;

    /// <summary>
    /// The series this run continued or started, with what it inherited, or null when it named none.
    /// </summary>
    /// <remarks>
    /// A report that is the sum of several runs says so here, beside the verdict, for the reason
    /// <see cref="Stopped"/> is there: its counts are not one run's, and a reader has to know that
    /// before reading them as one. See DEC0015.
    /// </remarks>
    public RunSeries? Series { get; }

    /// <summary>What each mutator family cost and bought, most mutants first.</summary>
    public IReadOnlyList<MutatorSummary> ByMutator { get; }

    /// <summary>What a reader must know before trusting the score - see <see cref="RunWarning"/>.</summary>
    public IReadOnlyList<RunWarning> Warnings { get; }
}
