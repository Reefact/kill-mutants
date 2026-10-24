using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class NegatedDecisionMutatorTests
{
    private static readonly NegatedDecisionMutator Mutator = new();

    /// <summary>
    /// Regression test (#18). Every decision the code acts on can be negated, not only one that was
    /// written with a <c>!</c>: the condition of each kind of branch and loop.
    /// </summary>
    [Theory]
    [InlineData("void M({0}) { if (a) { } }")]
    [InlineData("void M({0}) { while (a) { } }")]
    [InlineData("void M({0}) { do { } while (a); }")]
    [InlineData("void M({0}) { for (; a; ) { } }")]
    public void The_condition_of_a_branch_or_a_loop_is_negated(string member)
    {
        Assert.Equal(["!a"], MutatorTestBase.MutatedTexts(Mutator, "bool a", member));
    }

    /// <summary>A <c>bool</c> handed back is a decision too, however it is handed back.</summary>
    [Theory]
    [InlineData("bool M({0}) { return f(); }", "!f()")]
    [InlineData("bool M({0}) => f();", "!f()")]
    [InlineData("object M({0}) => System.Linq.Enumerable.Where(new[] { \"\" }, x => f());", "!f()")]
    public void A_returned_decision_is_negated(string member, string expected)
    {
        Assert.Equal(
            [expected], MutatorTestBase.MutatedTexts(Mutator, "System.Func<bool> f", member));
    }

    /// <summary>
    /// The operator binds tighter than anything it could be put in front of, so a compound decision
    /// is negated whole rather than in its first operand.
    /// </summary>
    [Fact]
    public void A_compound_decision_is_negated_whole()
    {
        Assert.Equal(
            ["!(a && b)"],
            MutatorTestBase.MutatedTexts(Mutator, "bool a, bool b", "bool M({0}) => a && b;"));
    }

    /// <summary>
    /// Three shapes belong to another family, which already makes the mutant: a comparison to
    /// <c>Comparison</c>, a literal to <c>BooleanLiteral</c>, and a ternary's condition to
    /// <c>Conditional</c>, whose branch swap is that negation.
    /// </summary>
    [Theory]
    [InlineData("bool M({0}) => x > y;")]
    [InlineData("bool M({0}) => (x == y);")]
    [InlineData("bool M({0}) => true;")]
    [InlineData("int M({0}) => x > y ? x : y;")]
    [InlineData("int M({0}) => b ? x : y;")]
    public void A_decision_another_family_already_negates_is_left_to_it(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "int x, int y, bool b", member));
    }

    /// <summary>
    /// A written negation is <c>Negation</c>'s to remove, and never doubled here: <c>!!a</c> is the
    /// original under another spelling, and could never be killed.
    /// </summary>
    [Fact]
    public void A_written_negation_is_not_negated_again()
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "bool a", "bool M({0}) => !a;"));
    }

    /// <summary>
    /// Only a <c>bool</c> used as one. A member returning <c>object</c> may hand back a
    /// <c>bool</c>, and negating it would change the type of what the caller receives.
    /// </summary>
    [Theory]
    [InlineData("object M({0}) => a;")]
    [InlineData("int M({0}) => x;")]
    public void A_value_that_is_not_a_decision_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "bool a, int x", member));
    }

    /// <summary>
    /// A new node of the target kind, as RB-001 requires of every family - not the original
    /// expression with an operator token written in front of it.
    /// </summary>
    [Fact]
    public void The_inserted_negation_is_a_new_node()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(
            Mutator, "bool a", "void M({0}) { if (a) { } }")[0];

        Assert.True(candidate.Replacement.IsKind(SyntaxKind.LogicalNotExpression));
    }

    /// <summary>
    /// A family of its own, so that <c>--without NegatedDecision</c> drops the insertion and keeps
    /// <c>Negation</c>'s removal.
    /// </summary>
    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "bool a", "void M({0}) { if (a) { } }"),
            candidate => Assert.Equal("NegatedDecision", candidate.Mutator.ToString()));
    }
}
