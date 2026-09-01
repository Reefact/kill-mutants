using Sample.Library;

namespace Sample.Library.Tests;

public class AgesTests
{
    [Fact]
    public void An_eighteen_year_old_is_an_adult()
    {
        Assert.True(Ages.IsAdult(18));
    }

    [Fact]
    public void A_seventeen_year_old_is_not()
    {
        Assert.False(Ages.IsAdult(17));
    }
}
