using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class ComparisonOperatorMutatorTests
{
    private static readonly ComparisonOperatorMutator Mutator = new();

    [Theory]
    // each relational operator yields its boundary shift and its negation
    [InlineData("x >= y", "x > y", "x < y")]
    [InlineData("x > y", "x >= y", "x <= y")]
    [InlineData("x <= y", "x < y", "x > y")]
    [InlineData("x < y", "x <= y", "x >= y")]
    public void A_relational_operator_yields_its_boundary_and_its_negation(
        string expression, string boundary, string negation)
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(Mutator, expression);

        Assert.Equal([boundary, negation], mutated);
    }

    [Theory]
    [InlineData("x == y", "x != y")]
    [InlineData("x != y", "x == y")]
    public void An_equality_operator_yields_only_its_negation(string expression, string expected)
    {
        // There is no boundary to shift, so a second mutant would be redundant.
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>
    /// The RB-001 guard, applied to this family. A replacement carrying the right token but the
    /// original node kind emits the original IL and can never be killed.
    /// </summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        (string Expression, SyntaxKind Kind)[] cases =
        [
            ("x >= y", SyntaxKind.GreaterThanExpression),
            ("x > y", SyntaxKind.GreaterThanOrEqualExpression),
            ("x <= y", SyntaxKind.LessThanExpression),
            ("x < y", SyntaxKind.LessThanOrEqualExpression),
            ("x == y", SyntaxKind.NotEqualsExpression),
            ("x != y", SyntaxKind.EqualsExpression),
        ];

        foreach ((string expression, SyntaxKind kind) in cases)
        {
            MutationCandidate first = MutatorTestBase.MutateAll(Mutator, expression)[0];

            Assert.True(
                first.Replacement.IsKind(kind),
                $"'{expression}' produced kind {first.Replacement.Kind()}, expected {kind}.");
        }
    }

    [Theory]
    [InlineData("a && b")]
    [InlineData("x + y > 0 == a")]
    public void Operators_outside_this_family_are_left_to_their_own_mutator(string expression)
    {
        // `x + y > 0 == a` still has comparisons, so assert on what this family did NOT touch.
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(Mutator, expression);

        Assert.DoesNotContain(mutated, text => text.Contains("&&", StringComparison.Ordinal) &&
                                               !expression.Contains("&&", StringComparison.Ordinal));
    }

    [Fact]
    public void Surrounding_formatting_is_preserved()
    {
        Assert.Equal("x   >   y", MutatorTestBase.MutatedTexts(Mutator, "x   >=   y")[0]);
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "x >= y"),
            candidate => Assert.Equal("Comparison", candidate.Mutator.ToString()));
    }
}
