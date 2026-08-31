using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Mutations;

public class MutationScoreTests
{
    [Fact]
    public void Every_mutant_killed_scores_one_hundred_percent()
    {
        MutationScore score = MutationScore.FromCounts(killed: 1, survived: 0);

        Assert.Equal(1d, score.Value);
        Assert.Equal("100%", score.ToString());
    }

    [Fact]
    public void No_mutant_killed_scores_zero()
    {
        MutationScore score = MutationScore.FromCounts(killed: 0, survived: 3);

        Assert.Equal(0d, score.Value);
        Assert.Equal("0%", score.ToString());
    }

    [Fact]
    public void A_score_with_a_fractional_part_keeps_two_decimals()
    {
        MutationScore score = MutationScore.FromCounts(killed: 2, survived: 1);

        Assert.Equal("66.67%", score.ToString());
    }

    [Fact]
    public void A_timed_out_mutant_counts_as_detected()
    {
        // A mutation that hangs the suite changed observable behaviour; the tests noticed.
        MutationScore score = MutationScore.FromCounts(killed: 0, survived: 0, timedOut: 1);

        Assert.Equal("100%", score.ToString());
    }

    [Fact]
    public void Nothing_tested_leaves_the_score_undefined()
    {
        MutationScore score = MutationScore.FromCounts(killed: 0, survived: 0);

        Assert.True(score.IsUndefined);
        Assert.Equal("n/a", score.ToString());
        Assert.True(double.IsNaN(score.Value));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Negative_counts_are_rejected(int killed, int survived, int timedOut)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MutationScore.FromCounts(killed, survived, timedOut));
    }
}
