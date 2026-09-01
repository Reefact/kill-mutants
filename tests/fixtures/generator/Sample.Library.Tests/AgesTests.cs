using Sample.Library;

namespace Sample.Library.Tests;

public class AgesTests
{
    [Theory]
    // the boundary, so that shifting >= to > is caught
    [InlineData(18, true)]
    [InlineData(17, false)]
    [InlineData(42, true)]
    public void Adulthood_starts_at_the_generated_limit(int age, bool expected)
    {
        Assert.Equal(expected, Ages.IsAdult(age));
    }
}
