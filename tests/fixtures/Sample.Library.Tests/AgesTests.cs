using Sample.Library;

namespace Sample.Library.Tests;

public class AgesTests
{
    [Theory]
    [InlineData(18)]
    [InlineData(42)]
    public void Adult_age_is_adult(int age)
    {
        Assert.True(Ages.IsAdult(age));
    }

    [Fact]
    public void Below_eighteen_is_not_adult()
    {
        Assert.False(Ages.IsAdult(17));
    }

    [Theory]
    // the boundary, so that shifting >= to > is caught
    [InlineData(18, true, true)]
    // below the boundary, so that negating the comparison is caught
    [InlineData(17, true, false)]
    // consent withheld, so that turning && into || is caught
    [InlineData(20, false, false)]
    public void Eligibility_needs_both_age_and_consent(int age, bool hasConsent, bool expected)
    {
        Assert.Equal(expected, Ages.IsEligible(age, hasConsent));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(30, false)]
    public void Minority_is_the_opposite_of_adulthood(int age, bool expected)
    {
        Assert.Equal(expected, Ages.IsMinor(age));
    }

    [Fact]
    public void Total_price_multiplies_unit_price_by_quantity()
    {
        Assert.Equal(12, Ages.TotalPrice(3, 4));
    }

    [Theory]
    [InlineData(30, "adult")]
    [InlineData(10, "minor")]
    public void Description_names_the_age_bracket(int age, string expected)
    {
        Assert.Equal(expected, Ages.Describe(age));
    }
}
