using System.Globalization;
using System.Runtime.InteropServices;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.Execution;

/// <summary>
/// Keeps the plan beside the report for one run: writes it before anything is discovered, completes
/// the identity once the mutants are selected, decides what an earlier run's verdicts may be
/// inherited, and rewrites the plan as the run learns what it reached.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of <see cref="MutationTestSession"/> so that the phase list there stays readable; the
/// session tells this what it selected and what it reached, and this decides what that means for the
/// series. See DEC0015 for what is inherited and DEC0016 for what is written.
/// </para>
/// <para>
/// A plan exists from the moment the run starts, because a run can be stopped before it has selected
/// anything and still writes a report. The first run of a series writes a preliminary plan - the
/// identity's known half, no mutants - at <see cref="Start"/>; a run that continues a series leaves
/// the earlier plan in place until <see cref="Begin"/>, since a refusal there has to leave it as it
/// was, and a stop before then records this run against the earlier population.
/// </para>
/// <para>
/// Two kinds of verdict are never inherited, whatever the log says. A timeout is final only under
/// the budget of the run that confirmed it, and this run's report can publish only the budgets this
/// run measured. A kill no test is named for is a crash, and a crash is the one verdict a stop can
/// manufacture - a test host killed by the same signal or tree kill as the tool reads exactly like
/// one the mutation crashed - so it is judged again, which costs one run and a genuine crash kill
/// is rare.
/// </para>
/// </remarks>
internal sealed class PlanKeeper
{
    private readonly RunContinuation _continuation;
    private readonly int _workerCount;
    private readonly string _started;
    private readonly int _logLinesBefore;
    private readonly List<PlannedDisagreement> _disagreements = [];

    private RunIdentity _identity;
    private IReadOnlyList<PlannedMutant>? _mutants;
    private int _killsReVerified;

    public PlanKeeper(RunContinuation continuation, int workerCount)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _continuation = continuation;
        _workerCount = workerCount;
        _started = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        // The verdicts read from the log are the lines it held - the caller read it with a torn
        // last line skipped, and opened it with the same line cut - so this is where this run's
        // lines begin, and where the previous run's end.
        _logLinesBefore = continuation.EarlierVerdicts.Count;

        // Until the mutants are selected, the plan describes what is known: for a continuing run,
        // the population the series already has; for a first run, the identity's known half and no
        // population.
        _identity = continuation.Earlier?.Identity ?? continuation.Identity;
        _mutants = continuation.Earlier?.Mutants;
    }

    /// <summary>
    /// Writes the first plan of a series before anything is discovered, so that a run stopped
    /// before it selected anything has a plan beside its report.
    /// </summary>
    /// <remarks>
    /// Nothing is written for a run that continues a series: the earlier plan is already there, and
    /// <see cref="Begin"/> may yet refuse to continue it, in which case it has to be found as it was.
    /// </remarks>
    /// <exception cref="IOException">The plan could not be written.</exception>
    public void Start()
    {
        if (_continuation.Earlier is null)
        {
            Write(reachedHere: 0);
        }
    }

    /// <summary>
    /// Completes the identity with what only a run that has selected its mutants can know, refuses
    /// to continue a series it would not continue honestly, inherits what may be inherited, and
    /// writes the plan.
    /// </summary>
    /// <param name="targets">The targets whose mutants were generated.</param>
    /// <param name="mutantsOf">Each target's mutants, in the order of <paramref name="targets"/>.</param>
    /// <param name="notGenerated">
    /// The targets discovery found but this run set aside before generating their mutants, at the
    /// build or the analysis (DEC0022).
    /// </param>
    /// <param name="scope">What population the run inspects.</param>
    /// <param name="root">The directory the run was pointed at.</param>
    /// <param name="testFramework">The xUnit the test applications were built on.</param>
    /// <param name="ledger">The verdicts, which inherit what may be inherited.</param>
    /// <exception cref="ResumeException">The series cannot be continued by this run.</exception>
    public void Begin(
        IReadOnlyList<MutationTestTarget> targets,
        IReadOnlyList<IReadOnlyList<Mutant>> mutantsOf,
        IReadOnlyList<MutationTestTarget> notGenerated,
        RunScope scope,
        string root,
        string? testFramework,
        VerdictLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(mutantsOf);
        ArgumentNullException.ThrowIfNull(notGenerated);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(ledger);

        IReadOnlyList<Mutant> all = [.. mutantsOf.SelectMany(mutants => mutants)];

        // The graph is discovery's, set-aside targets included: a target whose build failed is still
        // a project the suites reach, and leaving it out would make the run that recovers it look
        // like another codebase. Its mutants are what this run cannot know, and the plan says whose.
        RunIdentity identity = _continuation.Identity with
        {
            Scope = new PlannedScope(
                scope.IsPartial ? "partial" : "full",
                scope.ComparedFrom,
                scope.ComparedTo,
                scope.ComparedToIsExact),
            Fingerprint = RunIdentity.FingerprintOf(all.Select(mutant => mutant.Key.ToString())),
            TargetGraph = GraphOf([.. targets, .. notGenerated], root),
            TestFramework = testFramework,
            NotGenerated = notGenerated.Count == 0
                ? null
                : [.. notGenerated
                    .Select(target => Relative(target.ProjectUnderTest.ProjectPath, root))
                    .Order(StringComparer.Ordinal)],
        };

        if (_continuation.Earlier is { } earlier)
        {
            // Everything known before the run matched, or the caller would not have continued the
            // log. What differs here can only come from the build or the generation, and the user
            // did not cause it - so the files stay as they are, and the message names it.
            // A target the earlier run set aside before generating its mutants is the part a
            // resumed run exists to complete (DEC0022): its mutants are compared as new ones, and
            // the rest of the population has to be exactly what the earlier run selected.
            HashSet<string> recovered = new(earlier.Identity.NotGenerated ?? [], StringComparer.Ordinal);
            RunIdentity compared = recovered.Count == 0
                ? identity
                : identity with
                {
                    Fingerprint = RunIdentity.FingerprintOf(
                        targets
                            .Zip(mutantsOf)
                            .Where(pair => !recovered.Contains(Relative(pair.First.ProjectUnderTest.ProjectPath, root)))
                            .SelectMany(pair => pair.Second)
                            .Select(mutant => mutant.Key.ToString())),
                };

            if (compared.WhyNotContinuedBy(earlier.Identity) is { } why)
            {
                throw new ResumeException(
                    $"The series '{_continuation.Series}' cannot be continued by this run: {why}. " +
                    "The plan and the log were left as they were. Choose another series name, or " +
                    $"delete '{_continuation.Store.PlanPath}' to start this one again.");
            }

            Inherit(all, earlier, ledger);
        }

        _identity = identity;
        _mutants = [.. all.Select(Describe)];

        Write(ledger.ReachedHere);
    }

    /// <summary>Keeps a disagreement <c>--verify-kills</c> found, so the series carries it.</summary>
    public void Disagreed(MutantKey key, string disagreement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disagreement);

        _disagreements.Add(new PlannedDisagreement(key.ToString(), disagreement));
    }

    /// <summary>Rewrites the plan with what this run has reached so far.</summary>
    /// <remarks>
    /// Before <see cref="Begin"/> - a run stopped while discovering, building or generating - the
    /// plan written is the one the series had, with this run added to it and nothing reached.
    /// </remarks>
    public void Record(VerdictLedger ledger, int killsReVerified)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _killsReVerified = killsReVerified;

        Write(ledger.ReachedHere);
    }

    /// <summary>What the report says about the series, or null for a run that named none.</summary>
    public RunSeries? Series(VerdictLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return _continuation.Series is { } series
            ? new RunSeries(series, ledger.Inherited, Runs(ledger.ReachedHere))
            : null;
    }

    private void Inherit(IReadOnlyList<Mutant> all, RunPlan earlier, VerdictLedger ledger)
    {
        // The last line for a key wins. A kill no test is named for is judged again and lands again
        // under the same key, so the later line is the later run's.
        Dictionary<string, LoggedVerdict> logged = new(StringComparer.Ordinal);

        foreach (LoggedVerdict verdict in _continuation.EarlierVerdicts)
        {
            logged[verdict.Key] = verdict;
        }

        // Two mutants with one key are a population a reader cannot tell apart, so neither inherits
        // the line: both are judged again. About one chance in three million at ten thousand mutants.
        HashSet<string> shared = [.. all
            .GroupBy(mutant => mutant.Key.ToString(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        Dictionary<string, Mutant> inherited = new(StringComparer.Ordinal);

        foreach (Mutant mutant in all)
        {
            string key = mutant.Key.ToString();

            if (shared.Contains(key) ||
                !logged.TryGetValue(key, out LoggedVerdict? verdict) ||
                verdict.Status == MutantStatus.Timeout ||
                (verdict.Status == MutantStatus.Killed && verdict.KilledBy.Count == 0))
            {
                continue;
            }

            ledger.Inherit(new MutantResult(
                mutant, verdict.Status, verdict.Detail, verdict.KilledBy, Overturned: verdict.Overturned));

            inherited[key] = mutant;
        }

        foreach (PlannedDisagreement disagreement in earlier.Runs.SelectMany(run => run.Disagreements))
        {
            if (inherited.TryGetValue(disagreement.Key, out Mutant? mutant) &&
                ledger.VerdictOf(mutant) is { } held)
            {
                ledger.Amend(held with { Disagreement = disagreement.Disagreement });
            }
        }
    }

    private void Write(int reachedHere) =>
        _continuation.Store.WritePlan(new RunPlan(
            RunPlan.CurrentFormat, _continuation.Series, _identity, _mutants, Runs(reachedHere)));

    /// <summary>
    /// Every run of the series, the earlier ones with their counts taken from the log rather than
    /// from what they last wrote.
    /// </summary>
    /// <remarks>
    /// A run that is killed rewrote its plan at the end of the last project it finished, and the
    /// log took verdicts after that. The lines between where one run began and where the next did
    /// are the ones it wrote, and the last earlier run's end is where this one began - see
    /// <see cref="PlannedRun.LogLinesBefore"/>.
    /// </remarks>
    private List<PlannedRun> Runs(int reachedHere)
    {
        IReadOnlyList<PlannedRun> earlier = _continuation.Earlier?.Runs ?? [];
        List<PlannedRun> runs = new(earlier.Count + 1);

        for (int index = 0; index < earlier.Count; index++)
        {
            int end = index + 1 < earlier.Count ? earlier[index + 1].LogLinesBefore : _logLinesBefore;

            runs.Add(earlier[index] with { Verdicts = Math.Max(0, end - earlier[index].LogLinesBefore) });
        }

        runs.Add(new PlannedRun(
            _started,
            _continuation.Identity.ToolVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            _workerCount,
            _logLinesBefore,
            reachedHere,
            _killsReVerified,
            [.. _disagreements]));

        return runs;
    }

    /// <summary>
    /// Each project under test with the suites reaching it, by paths relative to the directory the
    /// run was pointed at, so that two checkouts of one commit describe the same graph.
    /// </summary>
    private static IReadOnlyList<string> GraphOf(IReadOnlyList<MutationTestTarget> targets, string root) =>
    [
        .. targets
            .Select(target =>
                $"{Relative(target.ProjectUnderTest.ProjectPath, root)} <- " +
                string.Join(
                    ", ",
                    target.TestProjects
                        .Select(test => Relative(test.ProjectPath, root))
                        .Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal),
    ];

    private static string Relative(string path, string root) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static PlannedMutant Describe(Mutant mutant) => new(
        mutant.Key.ToString(),
        mutant.Id.ToString(),
        mutant.Mutator.ToString(),
        mutant.RelativePath,
        mutant.Location.Line,
        mutant.Location.Character,
        mutant.OriginalText,
        mutant.MutatedText);
}
