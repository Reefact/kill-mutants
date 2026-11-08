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

    /// <summary>
    /// Rounded plainly, 20 937 of 20 938 is 100% to two decimals - and a suite was told it had
    /// caught everything with a mutant still alive, while <c>--break-at 100</c> let it through.
    /// </summary>
    [Fact]
    public void One_survivor_among_twenty_thousand_does_not_read_as_one_hundred_percent()
    {
        MutationScore score = MutationScore.FromCounts(detected: 20_937, undetected: 1);

        Assert.Equal("99.99%", score.ToString());
        Assert.Equal(99.99m, score.Percentage);
        Assert.False(score.Reaches(100));
        Assert.True(score.Reaches(99.99));
    }

    [Fact]
    public void One_kill_among_a_hundred_thousand_does_not_read_as_zero()
    {
        MutationScore score = MutationScore.FromCounts(detected: 1, undetected: 99_999);

        Assert.Equal("0.01%", score.ToString());
        Assert.True(score.Reaches(0.01));
    }

    /// <summary>The gate agrees with the line printed above it, to the hundredth.</summary>
    [Fact]
    public void A_score_reaches_the_threshold_it_prints()
    {
        MutationScore score = MutationScore.FromCounts(detected: 2, undetected: 1);

        Assert.True(score.Reaches(66.67));
        Assert.False(score.Reaches(66.68));
    }

    [Fact]
    public void An_undefined_score_reaches_no_threshold()
    {
        MutationScore score = MutationScore.FromCounts(detected: 0, undetected: 0);

        Assert.Null(score.Percentage);
        Assert.False(score.Reaches(0));
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
