using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class AssignmentOperatorMutatorTests
{
    private static readonly AssignmentOperatorMutator Mutator = new();

    [Theory]
    [InlineData("x += y", "x -= y")]
    [InlineData("x -= y", "x += y")]
    [InlineData("x *= y", "x /= y")]
    [InlineData("x /= y", "x *= y")]
    [InlineData("x %= y", "x *= y")]
    [InlineData("x &= y", "x |= y")]
    [InlineData("x |= y", "x &= y")]
    [InlineData("x <<= y", "x >>= y")]
    [InlineData("x >>= y", "x <<= y")]
    public void A_compound_assignment_is_replaced(string statement, string expected)
    {
        Assert.Equal([expected], Mutate(statement));
    }

    /// <summary>The RB-001 guard, applied to this family.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        (string Statement, SyntaxKind Kind)[] cases =
        [
            ("x += y", SyntaxKind.SubtractAssignmentExpression),
            ("x -= y", SyntaxKind.AddAssignmentExpression),
            ("x *= y", SyntaxKind.DivideAssignmentExpression),
            ("x /= y", SyntaxKind.MultiplyAssignmentExpression),
            ("x %= y", SyntaxKind.MultiplyAssignmentExpression),
            ("x &= y", SyntaxKind.OrAssignmentExpression),
            ("x |= y", SyntaxKind.AndAssignmentExpression),
            ("x <<= y", SyntaxKind.RightShiftAssignmentExpression),
            ("x >>= y", SyntaxKind.LeftShiftAssignmentExpression),
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
    /// `text += "x"` is the compound-assignment form of string concatenation, and `-=` does not
    /// exist for it. Same rule, same rejection as in the arithmetic family.
    /// </summary>
    [Fact]
    public void String_concatenation_is_not_mutated()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator, "string s, string t", "void M({0}) { s += t; }"));
    }

    /// <summary>A type declaring only `operator +` has no `-=` to swap in.</summary>
    [Fact]
    public void A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator, "Money left, Money right", "void M({0}) { left += right; }"));
    }

    /// <summary>A plain assignment has no operator to swap, so there is nothing to propose.</summary>
    [Fact]
    public void A_simple_assignment_is_left_alone()
    {
        Assert.Empty(Candidates("x = y"));
    }

    [Fact]
    public void Surrounding_formatting_is_preserved()
    {
        Assert.Equal("x   -=   y", Mutate("x   +=   y")[0]);
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            Candidates("x += y"),
            candidate => Assert.Equal("Assignment", candidate.Mutator.ToString()));
    }

    private static IReadOnlyList<MutationCandidate> Candidates(string statement) =>
        MutatorTestBase.MutateAll(Mutator, "int x, int y", "void M({0}) { " + statement + "; }");

    private static IReadOnlyList<string> Mutate(string statement) =>
        [.. Candidates(statement).Select(candidate => candidate.Replacement.ToString())];
}
