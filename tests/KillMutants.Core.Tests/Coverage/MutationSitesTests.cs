using KillMutants.Coverage;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Core.Tests.Coverage;

public class MutationSitesTests
{
    private static MutationSites SitesOf(string source)
    {
        Compilation compilation = TestCompilation.From(source);

        return MutationSites.From(
            new MutantGenerator(MutatorCatalog.Default).Generate(compilation), compilation);
    }

    [Fact]
    public void Mutants_sharing_an_expression_share_one_recorder()
    {
        // `age >= 18` yields a boundary shift and a negation: two mutants, one site.
        MutationSites sites = SitesOf("class C { bool M(int age) => age >= 18; }");

        Assert.Single(sites.IdentifierByNode);
        Assert.Equal(2, sites.RepresentativeOf.Count);
        Assert.Single(sites.RepresentativeOf.Values.Distinct());
        Assert.Empty(sites.Unmeasurable);
    }

    /// <summary>
    /// The probe is `T Hit&lt;T&gt;(int, T)`, and a ref struct cannot be a `T`. Verified against the
    /// .NET 10 SDK: wrapping a `Span&lt;int&gt;` yields CS9244. A conditional expression is a
    /// mutation site and may well have that type, so this is reachable in ordinary code - and the
    /// repair the compiler suggests, `allows ref struct`, needs a language version we do not control
    /// in the user's project.
    /// </summary>
    [Fact]
    public void A_site_whose_value_is_a_ref_struct_carries_no_recorder()
    {
        MutationSites sites = SitesOf(
            "class C { System.Span<int> M(bool flag, System.Span<int> a, System.Span<int> b) " +
            "=> flag ? a : b; }");

        // The mutant still exists - the branches can be swapped - it simply cannot be measured.
        Assert.NotEmpty(sites.RepresentativeOf);
        Assert.Empty(sites.IdentifierByNode);
        Assert.Single(sites.Unmeasurable);
    }

    /// <summary>
    /// The refusal must stay narrow, or coverage-driven selection would quietly stop selecting.
    /// </summary>
    [Fact]
    public void An_ordinary_expression_still_carries_one()
    {
        MutationSites sites = SitesOf("class C { bool M(int age) => age >= 18; }");

        Assert.Empty(sites.Unmeasurable);
        Assert.NotEmpty(sites.IdentifierByNode);
    }

    /// <summary>
    /// A pattern is made of compile-time constants, and a call is not one: instrumenting
    /// <c>s is "abc"</c> fails with CS9135. Found by reading Stryker.NET, which blocks injection in
    /// the same place, and then measuring. The mutation itself is fine and must survive the rule.
    /// </summary>
    /// <remarks>
    /// Asserted of the sites inside the pattern, not of every site in the snippet. The two were
    /// the same while the literal was the snippet's only site; negating the whole <c>is</c>
    /// expression (#18) added one outside the pattern, which carries a recorder as it should.
    /// </remarks>
    [Theory]
    [InlineData("class C { bool M(string s) => s is \"abc\"; }")]
    [InlineData("class C { int M(string s) => s switch { \"abc\" => 1, _ => 0 }; }")]
    public void A_site_inside_a_pattern_carries_no_recorder(string source)
    {
        MutationSites sites = SitesOf(source);

        Assert.NotEmpty(sites.RepresentativeOf);
        Assert.DoesNotContain(
            sites.IdentifierByNode.Keys,
            node => node.Ancestors().Any(ancestor => ancestor is PatternSyntax));
        Assert.NotEmpty(sites.Unmeasurable);
    }

    /// <summary>
    /// A `when` clause is not part of the pattern, so the rule must not swallow it.
    /// </summary>
    [Fact]
    public void A_site_in_a_when_clause_still_carries_one()
    {
        MutationSites sites = SitesOf(
            "class C { int M(object o) => o switch { int i when i >= 3 => i, _ => 0 }; }");

        Assert.NotEmpty(sites.IdentifierByNode);
        Assert.Empty(sites.Unmeasurable);
    }

    /// <summary>
    /// Regression test (RB-029). The compiler reads <c>while (true)</c> to conclude that the method
    /// owes no return after the loop. The probe is a call, so wrapping the constant takes that
    /// conclusion back and the instrumented build fails with CS0161, stopping the whole run. The
    /// mutant stays: it is simply not measured.
    /// </summary>
    [Fact]
    public void A_constant_the_compiler_reads_carries_no_recorder()
    {
        MutationSites sites = SitesOf(
            "class C { int M(bool a) { while (true) { if (a) { return 1; } } } }");

        Assert.DoesNotContain(
            sites.IdentifierByNode.Keys,
            node => node.IsKind(SyntaxKind.TrueLiteralExpression));
        Assert.NotEmpty(sites.Unmeasurable);
    }

    /// <summary>
    /// The refusal must stay narrow: a constant the compiler does not read keeps its recorder, and
    /// <c>return true;</c> is where most <c>BooleanLiteral</c> mutants live.
    /// </summary>
    [Theory]
    [InlineData("class C { bool M() { return true; } }")]
    [InlineData("class C { bool M(bool a) => a ? true : false; }")]
    [InlineData("class C { void M(System.Action<bool> f) { f(true); } }")]
    public void A_constant_the_compiler_does_not_read_still_carries_one(string source)
    {
        MutationSites sites = SitesOf(source);

        Assert.NotEmpty(sites.IdentifierByNode);
        Assert.Empty(sites.Unmeasurable);
    }

    [Fact]
    public void Every_mutant_keeps_a_representative_and_every_site_lands_in_one_bucket()
    {
        MutationSites sites = SitesOf(
            "class C { System.Span<int> M(bool f, System.Span<int> a, System.Span<int> b) => f ? a : b; " +
            "bool N(int age) => age >= 18; }");

        Assert.Equal(
            sites.RepresentativeOf.Values.Distinct().Count(),
            sites.IdentifierByNode.Count + sites.Unmeasurable.Count);
    }
}
