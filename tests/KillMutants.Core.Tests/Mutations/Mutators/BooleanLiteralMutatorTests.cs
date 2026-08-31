using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class BooleanLiteralMutatorTests
{
    private static readonly BooleanLiteralMutator Mutator = new();

    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public void Boolean_literals_are_swapped(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>The RB-001 guard, applied to this family.</summary>
    [Theory]
    [InlineData("true", (int)SyntaxKind.FalseLiteralExpression)]
    [InlineData("false", (int)SyntaxKind.TrueLiteralExpression)]
    public void Every_replacement_carries_the_kind_it_prints(string expression, int expectedKind)
    {
        MutationCandidate candidate = Assert.Single(MutatorTestBase.MutateAll(Mutator, expression));

        Assert.True(candidate.Replacement.IsKind((SyntaxKind)expectedKind));
    }

    [Theory]
    [InlineData("x >= y")]
    [InlineData("a")]
    public void Anything_that_is_not_a_boolean_literal_is_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }

    [Fact]
    public void Both_literals_in_one_expression_are_found()
    {
        Assert.Equal(2, MutatorTestBase.MutateAll(Mutator, "a ? true : false").Count);
    }
}
