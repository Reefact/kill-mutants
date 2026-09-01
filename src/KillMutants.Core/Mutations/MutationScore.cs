using System.Globalization;

namespace KillMutants.Mutations;

/// <summary>
/// The share of a suite's mutants that it detected. A value type rather than a loose double, so that
/// nobody divides two counters and formats a percentage by hand at a call site.
/// </summary>
public readonly record struct MutationScore
{
    private readonly int _detected;
    private readonly int _judged;

    private MutationScore(int detected, int judged)
    {
        _detected = detected;
        _judged = judged;
    }

    /// <summary>
    /// Computes the score from what the run established about the suite.
    /// </summary>
    /// <param name="detected">Mutants the suite noticed - see <see cref="MutantOutcome.Detected"/>.</param>
    /// <param name="undetected">Mutants it did not - see <see cref="MutantOutcome.Undetected"/>.</param>
    /// <remarks>
    /// <para>
    /// Only <see cref="MutantOutcome.Untestable"/> mutants are left out, and only because the suite
    /// was never asked about them: scoring this tool's own failure to build a mutant as a failure of
    /// the tests would be misleading. Everything the suite <em>was</em> asked about counts, including
    /// mutants no test reaches - those are undetected, and skipping their test run is an optimisation
    /// rather than a reason to forget them.
    /// </para>
    /// <para>
    /// This matches the definition mutation testing has always used, and Stryker.NET's implementation
    /// of it (<c>ProjectComponent.GetMutationScore</c>: detected over detected-plus-undetected, with
    /// <c>NoCoverage</c> undetected). Excluding uncovered mutants would mean a project could raise its
    /// score by adding code no test touches.
    /// </para>
    /// </remarks>
    public static MutationScore FromCounts(int detected, int undetected)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(detected);
        ArgumentOutOfRangeException.ThrowIfNegative(undetected);

        return new MutationScore(detected, detected + undetected);
    }

    /// <summary>True when no mutant could be judged, so no score is meaningful.</summary>
    public bool IsUndefined => _judged == 0;

    /// <summary>The score between 0 and 1. Undefined when nothing was judged.</summary>
    public double Value => IsUndefined ? double.NaN : (double)_detected / _judged;

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
