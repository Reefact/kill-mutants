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

    /// <summary>
    /// Regression test (#50). The name of an expression tree's parameter, variable or label shows
    /// only in a debug view, so emptying it could never be killed.
    /// </summary>
    [Theory]
    [InlineData("object M(string s) => System.Linq.Expressions.Expression.Parameter(typeof(int), \"dto\");")]
    [InlineData("object M(string s) => System.Linq.Expressions.Expression.Variable(typeof(object), \"boxed\");")]
    [InlineData("object M(string s) => System.Linq.Expressions.Expression.Label(\"end\");")]
    public void The_name_of_an_expression_tree_node_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "string", member));
    }

    /// <summary>A <c>name</c> argument anywhere else is text someone may read, and keeps its mutant.</summary>
    [Fact]
    public void A_name_passed_to_another_method_keeps_its_mutant()
    {
        Assert.Equal(
            ["\"\""],
            MutatorTestBase.MutatedTexts(Mutator, "string", "object M(string s) => N(name: \"dto\"); static object N(string name) => name;"));
    }
}
