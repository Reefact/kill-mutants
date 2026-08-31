using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Mutations;

public class MutatorNameTests
{
    [Fact]
    public void A_name_renders_as_the_text_it_was_created_from()
    {
        Assert.Equal("GreaterThanOrEqual", MutatorName.Create("GreaterThanOrEqual").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => MutatorName.Create(value));
    }
}
