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
}
