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

    /// <summary>
    /// Removal stays this family's whole job: negating a decision that has no <c>!</c> is
    /// <c>NegatedDecision</c>'s (#18), so that a project can drop one without the other.
    /// </summary>
    [Fact]
    public void A_decision_without_a_negation_is_left_to_its_own_family()
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "bool a", "void M({0}) { if (a) { } }"));
    }
}
