using System.Globalization;
using System.Runtime.InteropServices;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.Execution;

/// <summary>
/// Keeps the plan beside the report for one run: completes the identity once the mutants are
/// selected, decides what an earlier run's verdicts may be inherited, and rewrites the plan as the
/// run learns what it reached.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of <see cref="MutationTestSession"/> so that the phase list there stays readable; the
/// session tells this what it selected and what it reached, and this decides what that means for the
/// series. See DEC0015 for what is inherited and DEC0016 for what is written.
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
    private readonly List<PlannedDisagreement> _disagreements = [];

    private RunIdentity? _identity;
    private IReadOnlyList<PlannedMutant> _mutants = [];
    private int _killsReVerified;

    public PlanKeeper(RunContinuation continuation, int workerCount)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _continuation = continuation;
        _workerCount = workerCount;
        _started = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Completes the identity with what only a run that has selected its mutants can know, refuses
    /// to continue a series it would not continue honestly, inherits what may be inherited, and
    /// writes the plan.
    /// </summary>
    /// <exception cref="ResumeException">The series cannot be continued by this run.</exception>
    public void Begin(
        IReadOnlyList<Mutant> all,
        IReadOnlyList<MutationTestTarget> targets,
        RunScope scope,
        string root,
        string? testFramework,
        VerdictLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(ledger);

        _identity = _continuation.Identity with
        {
            Scope = new PlannedScope(
                scope.IsPartial ? "partial" : "full",
                scope.ComparedFrom,
                scope.ComparedTo,
                scope.ComparedToIsExact),
            Fingerprint = RunIdentity.FingerprintOf(all.Select(mutant => mutant.Key.ToString())),
            TargetGraph = GraphOf(targets, root),
            TestFramework = testFramework,
        };

        if (_continuation.Earlier is { } earlier)
        {
            // Everything known before the run matched, or the caller would not have continued the
            // log. What differs here can only come from the build or the generation, and the user
            // did not cause it - so the files stay as they are, and the message names it.
            if (_identity.WhyNotContinuedBy(earlier.Identity) is { } why)
            {
                throw new ResumeException(
                    $"The series '{_continuation.Series}' cannot be continued by this run: {why}. " +
                    $"Nothing was changed. Choose another series name, or delete " +
                    $"'{_continuation.Store.PlanPath}' to start this one again.");
            }

            Inherit(all, earlier, ledger);
        }

        _mutants = [.. all.Select(Describe)];

        Write(ledger);
    }

    /// <summary>Keeps a disagreement <c>--verify-kills</c> found, so the series carries it.</summary>
    public void Disagreed(MutantKey key, string disagreement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disagreement);

        _disagreements.Add(new PlannedDisagreement(key.ToString(), disagreement));
    }

    /// <summary>Rewrites the plan with what this run has reached so far.</summary>
    public void Record(VerdictLedger ledger, int killsReVerified)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _killsReVerified = killsReVerified;

        if (_identity is not null)
        {
            Write(ledger);
        }
    }

    /// <summary>What the report says about the series, or null for a run that named none.</summary>
    public RunSeries? Series(VerdictLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return _continuation.Series is { } series
            ? new RunSeries(series, ledger.Inherited, Runs(ledger))
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

    private void Write(VerdictLedger ledger) =>
        _continuation.Store.WritePlan(new RunPlan(
            RunPlan.CurrentFormat, _continuation.Series, _identity!, _mutants, Runs(ledger)));

    private IReadOnlyList<PlannedRun> Runs(VerdictLedger ledger) =>
    [
        .. _continuation.Earlier?.Runs ?? [],
        new PlannedRun(
            _started,
            _continuation.Identity.ToolVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            _workerCount,
            ledger.ReachedHere,
            _killsReVerified,
            [.. _disagreements]),
    ];

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
