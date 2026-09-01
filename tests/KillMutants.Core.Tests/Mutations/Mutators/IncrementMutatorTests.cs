using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class IncrementMutatorTests
{
    private static readonly IncrementMutator Mutator = new();

    [Theory]
    [InlineData("x++", "x--")]
    [InlineData("x--", "x++")]
    [InlineData("++x", "--x")]
    [InlineData("--x", "++x")]
    public void An_increment_is_replaced_by_its_opposite(string statement, string expected)
    {
        Assert.Equal([expected], Mutate(statement));
    }

    /// <summary>The RB-001 guard, applied to this family, in both positions.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        (string Statement, SyntaxKind Kind)[] cases =
        [
            ("x++", SyntaxKind.PostDecrementExpression),
            ("x--", SyntaxKind.PostIncrementExpression),
            ("++x", SyntaxKind.PreDecrementExpression),
            ("--x", SyntaxKind.PreIncrementExpression),
        ];

        foreach ((string statement, SyntaxKind kind) in cases)
        {
            MutationCandidate first = Candidates(statement)[0];

            Assert.True(
                first.Replacement.IsKind(kind),
                $"'{statement}' produced kind {first.Replacement.Kind()}, expected {kind}.");
        }
    }

    /// <summary>
    /// The position is part of the meaning: `x++` and `++x` differ in what the expression evaluates
    /// to, so a prefix must stay a prefix.
    /// </summary>
    [Fact]
    public void The_position_of_the_operator_is_preserved()
    {
        Assert.StartsWith("--", Mutate("++x")[0], StringComparison.Ordinal);
        Assert.EndsWith("--", Mutate("x++")[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x = -x")]
    [InlineData("x = +x")]
    public void Unary_signs_are_left_alone(string statement)
    {
        Assert.Empty(Candidates(statement));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            Candidates("x++"),
            candidate => Assert.Equal("Increment", candidate.Mutator.ToString()));
    }

    private static IReadOnlyList<MutationCandidate> Candidates(string statement) =>
        MutatorTestBase.MutateAll(Mutator, "int x", "void M({0}) { " + statement + "; }");

    private static IReadOnlyList<string> Mutate(string statement) =>
        [.. Candidates(statement).Select(candidate => candidate.Replacement.ToString())];
}
