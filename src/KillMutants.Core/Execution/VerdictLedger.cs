using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.Execution;

/// <summary>The verdicts a run has reached, recorded as each one becomes final.</summary>
/// <remarks>
/// <para>
/// The one place a verdict becomes final, so the report and whatever is told of verdicts as they
/// land cannot disagree about which mutants were judged. A run that ends normally reaches every
/// mutant it selected; one that was stopped reaches some, and its report is built from exactly those.
/// See DEC0013 and DEC0014.
/// </para>
/// <para>
/// Final means nothing later in the run replaces the status. A timeout is not final when it is first
/// seen: it is re-run alone before it is believed, and may turn out to be something else. A kill is
/// final before it is re-verified, because re-verifying never changes a status; what it can add is a
/// disagreement, which <see cref="Amend"/> records without announcing the verdict a second time.
/// </para>
/// </remarks>
internal sealed class VerdictLedger(IProgress<MutantResult>? observer)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Mutant, MutantResult> _reached = new();
    private readonly List<Mutant> _selected = [];
    private bool _hasSelected;

    /// <summary>How many mutants the run selected, or null until it has generated them.</summary>
    public int? Selected
    {
        get
        {
            lock (_gate)
            {
                return _hasSelected ? _selected.Count : null;
            }
        }
    }

    /// <summary>
    /// True once the run has selected its mutants and every one of them has a final verdict.
    /// </summary>
    public bool HasReachedEverySelected
    {
        get
        {
            lock (_gate)
            {
                return _hasSelected && _reached.Count == _selected.Count;
            }
        }
    }

    /// <summary>Records every mutant the run will try to judge, in the order it generated them.</summary>
    public void Select(IEnumerable<Mutant> mutants)
    {
        ArgumentNullException.ThrowIfNull(mutants);

        lock (_gate)
        {
            _selected.AddRange(mutants);
            _hasSelected = true;
        }
    }

    /// <summary>Records a final verdict, and tells the observer.</summary>
    public void Reach(MutantResult verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        lock (_gate)
        {
            _reached[verdict.Mutant] = verdict;
        }

        observer?.Report(verdict);
    }

    /// <summary>Replaces a verdict already reached with an annotated one, telling nobody.</summary>
    public void Amend(MutantResult verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        lock (_gate)
        {
            _reached[verdict.Mutant] = verdict;
        }
    }

    /// <summary>Every verdict reached, in the order the mutants were generated.</summary>
    /// <remarks>
    /// Generation order rather than the order they landed in, so a report does not depend on which
    /// worker happened to finish first.
    /// </remarks>
    public IReadOnlyList<MutantResult> InOrder()
    {
        lock (_gate)
        {
            return [.. _selected.Where(_reached.ContainsKey).Select(mutant => _reached[mutant])];
        }
    }
}
