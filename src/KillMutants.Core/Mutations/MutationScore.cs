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
    /// The score as a percentage to two decimals - the number printed, written in the report and
    /// compared with a threshold - or null when nothing was judged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rounded to the nearest hundredth except at the two ends, which are kept for the scores that
    /// are exactly there: <c>100</c> only when every mutant was detected, <c>0</c> only when none
    /// was. Plain rounding printed one survivor among twenty thousand as <c>100%</c>, and the gate,
    /// which allowed the same half-hundredth so as to agree with what it printed, let it through
    /// <c>--break-at 100</c>: a suite told it had caught everything, by the one number a reader
    /// looks at, with a mutant still alive. A score just short of an end reads <c>99.99%</c> or
    /// <c>0.01%</c> instead, which is the nearest thing two decimals can say that is not false.
    /// </para>
    /// <para>
    /// Counted in whole hundredths, with integer arithmetic, rather than rounded as a double: a
    /// threshold is compared with this, and <c>66.67</c> computed is not always the
    /// <c>66.67</c> a person wrote, so a double could refuse a score that printed exactly the
    /// threshold.
    /// </para>
    /// </remarks>
    public decimal? Percentage => IsUndefined ? null : Hundredths / 100m;

    private long Hundredths
    {
        get
        {
            // To the nearest hundredth of a percent, a half rounded up.
            long hundredths = (20_000L * _detected + _judged) / (2L * _judged);

            return _detected == _judged ? 10_000
                : _detected == 0 ? 0
                : Math.Clamp(hundredths, 1, 9_999);
        }
    }

    /// <summary>
    /// True when the score, as printed, is at least <paramref name="thresholdPercent"/>; never when
    /// nothing was judged.
    /// </summary>
    /// <remarks>
    /// The printed score and not the exact ratio, so that the gate and the line above it agree: a
    /// run that reads <c>66.67%</c> passes <c>--break-at 66.67</c>, and one that reads
    /// <c>99.99%</c> does not pass <c>--break-at 100</c>. An undefined score reaches nothing - a run
    /// that tested nothing has demonstrated nothing (DEC0009).
    /// </remarks>
    public bool Reaches(double thresholdPercent) =>
        Percentage is { } percentage && percentage >= (decimal)thresholdPercent;

    /// <summary>
    /// Renders the score as a percentage: <c>100%</c>, <c>66.67%</c>, or <c>n/a</c> when undefined.
    /// </summary>
    public override string ToString() =>
        Percentage is { } percentage
            ? percentage.ToString("0.##", CultureInfo.InvariantCulture) + "%"
            : "n/a";
}
