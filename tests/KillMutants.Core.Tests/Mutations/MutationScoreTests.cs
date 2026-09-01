using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Mutations;

public class MutationScoreTests
{
    [Fact]
    public void Every_mutant_detected_scores_one_hundred_percent()
    {
        MutationScore score = MutationScore.FromCounts(detected: 1, undetected: 0);

        Assert.Equal(1d, score.Value);
        Assert.Equal("100%", score.ToString());
    }

    [Fact]
    public void Nothing_detected_scores_zero()
    {
        MutationScore score = MutationScore.FromCounts(detected: 0, undetected: 3);

        Assert.Equal(0d, score.Value);
        Assert.Equal("0%", score.ToString());
    }

    [Fact]
    public void A_score_with_a_fractional_part_keeps_two_decimals()
    {
        Assert.Equal("66.67%", MutationScore.FromCounts(detected: 2, undetected: 1).ToString());
    }

    [Fact]
    public void Nothing_judged_leaves_the_score_undefined()
    {
        MutationScore score = MutationScore.FromCounts(detected: 0, undetected: 0);

        Assert.True(score.IsUndefined);
        Assert.Equal("n/a", score.ToString());
        Assert.True(double.IsNaN(score.Value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Negative_counts_are_rejected(int detected, int undetected)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MutationScore.FromCounts(detected, undetected));
    }
}
