using System.Globalization;

namespace KillMutants.Mutations;

/// <summary>
/// The share of tested mutants the test suite caught. A value type rather than a loose double, so
/// that nobody divides two counters and formats a percentage by hand at a call site.
/// </summary>
public readonly record struct MutationScore
{
    private readonly int _killed;
    private readonly int _tested;

    private MutationScore(int killed, int tested)
    {
        _killed = killed;
        _tested = tested;
    }

    /// <summary>
    /// Computes the score from the mutants that were actually tested. Mutants that could not be
    /// tested at all (compile errors, uncovered code) are excluded: scoring a tool's own failure to
    /// build a mutant as a testing failure would be misleading.
    /// </summary>
    public static MutationScore FromCounts(int killed, int survived, int timedOut = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(killed);
        ArgumentOutOfRangeException.ThrowIfNegative(survived);
        ArgumentOutOfRangeException.ThrowIfNegative(timedOut);

        // A timeout means the mutant changed observable behaviour enough to hang the suite,
        // which is a detection, not a survival.
        return new MutationScore(killed + timedOut, killed + survived + timedOut);
    }

    /// <summary>True when no mutant could be tested, so no score is meaningful.</summary>
    public bool IsUndefined => _tested == 0;

    /// <summary>The score between 0 and 1. Undefined when nothing was tested.</summary>
    public double Value => IsUndefined ? double.NaN : (double)_killed / _tested;

    /// <summary>
    /// Renders the score as a percentage: <c>100%</c>, <c>66.67%</c>, or <c>n/a</c> when undefined.
    /// </summary>
    public override string ToString()
    {
        if (IsUndefined)
        {
            return "n/a";
        }

        double percentage = Value * 100d;
        string rendered = Math.Abs(percentage - Math.Round(percentage)) < 0.005d
            ? Math.Round(percentage).ToString("0", CultureInfo.InvariantCulture)
            : percentage.ToString("0.##", CultureInfo.InvariantCulture);

        return rendered + "%";
    }
}
