using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class NegationMutatorTests
{
    private static readonly NegationMutator Mutator = new();

    [Theory]
    [InlineData("!a", "a")]
    [InlineData("!(x >= y)", "(x >= y)")]
    public void A_logical_negation_is_removed(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("x >= y")]
    public void An_expression_without_negation_is_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }

    [Fact]
    public void The_replacement_takes_over_the_whole_expression_formatting()
    {
        // The operand inherits the negation's own trivia, so `! a` collapses to `a` rather than
        // leaving the space that sat between the operator and its operand.
        Assert.Equal(["a"], MutatorTestBase.MutatedTexts(Mutator, "! a"));
    }
}
