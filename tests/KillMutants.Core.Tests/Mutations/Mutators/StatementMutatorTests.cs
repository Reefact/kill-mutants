using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>A statement that only calls something is removed, and nothing else is - DEC0020.</summary>
public class StatementMutatorTests
{
    private static readonly StatementMutator Mutator = new();

    [Theory]
    [InlineData("void M(System.Action f) { f(); }", "f();")]
    [InlineData("void M(System.Text.StringBuilder b) { b.Append('x'); }", "b.Append('x');")]
    [InlineData("void M(System.Text.StringBuilder? b) { b?.Clear(); }", "b?.Clear();")]
    [InlineData("async System.Threading.Tasks.Task M(System.Func<System.Threading.Tasks.Task> f) { await f(); }", "await f();")]
    [InlineData("void M() { new System.Text.StringBuilder(); }", "new System.Text.StringBuilder();")]
    public void A_call_statement_becomes_an_empty_block(string member, string removed)
    {
        Assert.Equal(
            [(removed, "{}")],
            MutatorTestBase.MutateAll(Mutator, "", member)
                .Select(candidate => (candidate.Original.ToString(), candidate.Replacement.ToString())));
    }

    /// <summary>
    /// A block rather than an empty statement, because <c>if (a) ;</c> draws CS0642 and a block is
    /// legal wherever the call stood.
    /// </summary>
    [Fact]
    public void The_body_of_an_if_without_braces_stays_a_statement()
    {
        Assert.Equal(["{}"], MutatorTestBase.MutatedTexts(Mutator, "", "void M(bool a, System.Action f) { if (a) f(); }"));
    }

    [Theory]
    [InlineData("int M(System.Func<int> f) { int x; x = f(); return x; }")]
    [InlineData("void M(int x) { x++; }")]
    [InlineData("void M(int x) { x += 2; }")]
    [InlineData("int M(string s) { int n; int.TryParse(s, out n); return n; }")]
    [InlineData("void M(bool a) { System.Diagnostics.Debug.Assert(a); }")]
    [InlineData("int M(System.Func<int> f) { return f(); }")]
    [InlineData("void M() { throw new System.InvalidOperationException(); }")]
    [InlineData("System.Action M(System.Action f) => () => f();")]
    public void What_is_not_a_plain_call_statement_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "", member));
    }

    /// <summary>
    /// Calls the compiler drops by itself, so removing them changes nothing and their mutant could
    /// never be killed: a <c>[Conditional]</c> method reached through <c>?.</c>, and a partial
    /// method nobody implemented.
    /// </summary>
    [Theory]
    [InlineData(
        "public class Log { [System.Diagnostics.Conditional(\"NEVER_DEFINED\")] public void Trace() { } } " +
        "public class C { public void M(Log? log) { log?.Trace(); } }")]
    [InlineData(
        "public partial class C { partial void OnChanged(); public void M() { OnChanged(); } }")]
    public void A_call_the_compiler_drops_is_left_alone(string source)
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(source);

        Assert.Empty(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }

    /// <summary>An implemented partial method is called for real, so removing the call is a mutant.</summary>
    [Fact]
    public void A_call_to_an_implemented_partial_method_is_removed()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "public partial class C { partial void OnChanged(); partial void OnChanged() { } " +
            "public void M() { OnChanged(); } }");

        Assert.Single(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }

    /// <summary>
    /// Regression test (#49). <c>EnableConcurrentExecution()</c> only lets the host run an analyzer's
    /// callbacks in parallel, which no assertion on its diagnostics can see; the line beside it,
    /// <c>ConfigureGeneratedCodeAnalysis</c>, changes what is reported and keeps its mutant.
    /// </summary>
    [Fact]
    public void An_analyzers_EnableConcurrentExecution_is_left_alone()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "using Microsoft.CodeAnalysis.Diagnostics; " +
            "public class C { public void Initialize(AnalysisContext context) { " +
            "context.EnableConcurrentExecution(); " +
            "context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None); } }");

        MutationCandidate candidate = Assert.Single(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
        Assert.Contains("ConfigureGeneratedCodeAnalysis", candidate.Original.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A method that is only called <c>EnableConcurrentExecution</c> is someone's code, and its call is removed.</summary>
    [Fact]
    public void An_EnableConcurrentExecution_of_another_type_is_removed()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "public class Pool { public void EnableConcurrentExecution() { } } " +
            "public class C { public void M(Pool pool) { pool.EnableConcurrentExecution(); } }");

        Assert.Single(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }
}
