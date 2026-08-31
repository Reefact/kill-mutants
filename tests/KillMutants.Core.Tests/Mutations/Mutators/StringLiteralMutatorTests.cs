using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class StringLiteralMutatorTests
{
    private static readonly StringLiteralMutator Mutator = new();

    [Fact]
    public void A_non_empty_literal_is_emptied()
    {
        Assert.Equal(
            ["\"\""],
            MutatorTestBase.MutatedTexts(Mutator, "string", "object M(string s) => \"hello\";"));
    }

    [Fact]
    public void An_empty_literal_is_filled()
    {
        // Emptying an already-empty literal would change nothing, so the mutation goes the other way.
        Assert.Equal(
            ["\"KillMutants\""],
            MutatorTestBase.MutatedTexts(Mutator, "string", "object M(string s) => \"\";"));
    }

    [Theory]
    [InlineData("object M(string s) => 42;")]
    [InlineData("object M(string s) => 'c';")]
    [InlineData("object M(string s) => true;")]
    public void Anything_that_is_not_a_string_literal_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "string", member));
    }
}
