using System.Globalization;
using CsCheck;
using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// The score is the number a reader looks at and the number <c>--break-at</c> gates on, so what it
/// promises has to hold for every count a run can produce, not for the three a person writes down:
/// the round number never lies, the printed figure is the nearest one two decimals can give, and
/// the gate passes exactly when that figure reaches the threshold.
/// </summary>
public class MutationScorePropertyTests
{
    /// <summary>
    /// Counts from a handful to a few hundred thousand, the large ones being where one mutant is
    /// smaller than a hundredth of a percent.
    /// </summary>
    private static readonly Gen<(int Detected, int Undetected)> Counts =
        Gen.Select(
                Gen.OneOf(Gen.Int[0, 50], Gen.Int[0, 500_000]),
                Gen.OneOf(Gen.Int[0, 50], Gen.Int[0, 500_000]))
            .Where((detected, undetected) => detected + undetected > 0);

    private static double Printed(MutationScore score) =>
        double.Parse(score.ToString().TrimEnd('%'), CultureInfo.InvariantCulture);

    [Fact]
    public void The_round_numbers_are_printed_only_for_the_scores_that_are_round()
    {
        Counts.Sample(
            (detected, undetected) =>
            {
                MutationScore score = MutationScore.FromCounts(detected, undetected);

                Assert.Equal(undetected == 0, score.ToString() == "100%");
                Assert.Equal(detected == 0, score.ToString() == "0%");
            },
            iter: 20_000);
    }

    /// <summary>
    /// Within half a hundredth of the exact score, as rounding is - or within a hundredth for the
    /// few kept off the two ends.
    /// </summary>
    [Fact]
    public void The_printed_score_is_the_exact_one_to_two_decimals()
    {
        Counts.Sample(
            (detected, undetected) =>
            {
                MutationScore score = MutationScore.FromCounts(detected, undetected);
                double exact = 100d * detected / (detected + undetected);
                double printed = Printed(score);
                bool keptOffAnEnd = printed is 99.99 or 0.01;

                Assert.InRange(Math.Abs(printed - exact), 0, keptOffAnEnd ? 0.01 : 0.005 + 1e-9);
                Assert.Equal((double)score.Percentage!.Value, printed);
            },
            iter: 20_000);
    }

    /// <summary>
    /// The threshold drawn as a person writes one, to the hundredth: the gate passes exactly when
    /// the figure printed above it is at least that.
    /// </summary>
    [Fact]
    public void The_gate_passes_exactly_when_the_printed_score_reaches_the_threshold()
    {
        Gen.Select(Counts, Gen.Int[0, 10_000]).Sample(
            (counts, hundredths) =>
            {
                MutationScore score = MutationScore.FromCounts(counts.Detected, counts.Undetected);
                double threshold = hundredths / 100d;

                Assert.Equal(Printed(score) >= threshold, score.Reaches(threshold));
            },
            iter: 20_000);
    }
}
