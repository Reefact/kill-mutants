using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class LogicalOperatorMutatorTests
{
    private static readonly LogicalOperatorMutator Mutator = new();

    [Theory]
    [InlineData("a && b", "a || b")]
    [InlineData("a || b", "a && b")]
    public void Conditional_operators_are_swapped(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>The RB-001 guard, applied to this family.</summary>
    [Theory]
    [InlineData("a && b", (int)SyntaxKind.LogicalOrExpression)]
    [InlineData("a || b", (int)SyntaxKind.LogicalAndExpression)]
    public void Every_replacement_carries_the_kind_it_prints(string expression, int expectedKind)
    {
        MutationCandidate candidate = Assert.Single(MutatorTestBase.MutateAll(Mutator, expression));

        Assert.True(candidate.Replacement.IsKind((SyntaxKind)expectedKind));
    }

    [Theory]
    [InlineData("x >= y")]
    [InlineData("a == b")]
    public void Other_operators_are_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }
}
