using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class GreaterThanOrEqualMutatorTests
{
    private static BinaryExpressionSyntax FirstBinaryExpression(string expression)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText($"class C {{ bool M(int a, int b) => {expression}; }}");

        return tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().First();
    }

    private static MutationCandidate? MutateFirst(string expression)
    {
        var mutator = new GreaterThanOrEqualMutator();

        return mutator.Mutate(FirstBinaryExpression(expression)).SingleOrDefault();
    }

    [Fact]
    public void Greater_than_or_equal_is_mutated_to_greater_than()
    {
        MutationCandidate? candidate = MutateFirst("a >= b");

        Assert.NotNull(candidate);
        Assert.Equal("a > b", candidate.Replacement.ToString());
    }

    /// <summary>
    /// The regression test for the defect that motivated this mutator's design.
    /// Swapping only the operator token leaves the node kind at GreaterThanOrEqualExpression.
    /// Roslyn emits from the node kind, so such a "mutant" prints as `a > b` but compiles to
    /// exactly the original IL - silently equivalent, and always reported as survived.
    /// </summary>
    [Fact]
    public void The_replacement_node_has_the_greater_than_kind_not_merely_a_greater_than_token()
    {
        MutationCandidate? candidate = MutateFirst("a >= b");

        Assert.NotNull(candidate);
        Assert.True(candidate.Replacement.IsKind(SyntaxKind.GreaterThanExpression));
        Assert.False(candidate.Replacement.IsKind(SyntaxKind.GreaterThanOrEqualExpression));
    }

    [Theory]
    [InlineData("a > b")]
    [InlineData("a <= b")]
    [InlineData("a < b")]
    [InlineData("a == b")]
    [InlineData("a != b")]
    [InlineData("a + b > 0")]
    public void Other_operators_are_left_alone(string expression)
    {
        // Milestone 1 ships exactly one mutation. Everything else must be untouched,
        // so that a growing catalog cannot silently widen this mutator's reach.
        Assert.Null(MutateFirst(expression));
    }

    [Fact]
    public void Surrounding_formatting_is_preserved()
    {
        MutationCandidate? candidate = MutateFirst("a   >=   b");

        Assert.NotNull(candidate);
        Assert.Equal("a   >   b", candidate.Replacement.ToString());
    }

    [Fact]
    public void The_mutation_is_attributed_to_the_named_mutator()
    {
        MutationCandidate? candidate = MutateFirst("a >= b");

        Assert.NotNull(candidate);
        Assert.Equal("GreaterThanOrEqual", candidate.Mutator.ToString());
    }
}
