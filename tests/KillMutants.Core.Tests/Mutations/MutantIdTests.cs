using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Mutations;

public class MutantIdTests
{
    [Fact]
    public void Identifiers_run_from_one_and_increment()
    {
        MutantId first = MutantId.First;
        MutantId second = first.Next();

        Assert.Equal("M1", first.ToString());
        Assert.Equal("M2", second.ToString());
    }

    [Fact]
    public void Identifiers_compare_by_value()
    {
        Assert.Equal(MutantId.First, MutantId.First);
        Assert.NotEqual(MutantId.First, MutantId.First.Next());
    }
}
