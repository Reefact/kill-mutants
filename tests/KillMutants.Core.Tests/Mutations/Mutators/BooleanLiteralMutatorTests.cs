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

    /// <summary>
    /// Regression test (#48). The argument of a task's <c>ConfigureAwait</c> only decides whether the
    /// continuation returns to a <c>SynchronizationContext</c> test code does not run under, so its
    /// mutant could never be killed.
    /// </summary>
    [Theory]
    [InlineData("async System.Threading.Tasks.Task M(System.Threading.Tasks.Task t) { await t.ConfigureAwait(false); }")]
    [InlineData("async System.Threading.Tasks.Task<int> M(System.Threading.Tasks.Task<int> t) => await t.ConfigureAwait(true);")]
    [InlineData("async System.Threading.Tasks.ValueTask M(System.Threading.Tasks.ValueTask t) { await t.ConfigureAwait(false); }")]
    public void The_argument_of_a_tasks_ConfigureAwait_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "", member));
    }

    /// <summary>
    /// The extension <c>ConfigureAwait</c> of an async stream is the same choice, reached through
    /// <c>TaskAsyncEnumerableExtensions</c>.
    /// </summary>
    [Fact]
    public void The_argument_of_an_async_streams_ConfigureAwait_is_left_alone()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "using System.Threading.Tasks; " +
            "public class C { public async Task M(System.Collections.Generic.IAsyncEnumerable<int> e) { " +
            "await foreach (int i in e.ConfigureAwait(false)) { } } }");

        Assert.Empty(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }

    /// <summary>
    /// The exemption is bound through the compiler: a method that is only called <c>ConfigureAwait</c>
    /// is someone's code, and its argument keeps its mutant.
    /// </summary>
    [Fact]
    public void A_ConfigureAwait_of_another_type_keeps_its_mutant()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "public class Pipe { public Pipe ConfigureAwait(bool continueOnCapturedContext) => this; } " +
            "public class C { public Pipe M(Pipe p) => p.ConfigureAwait(false); }");

        Assert.Single(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }
}
