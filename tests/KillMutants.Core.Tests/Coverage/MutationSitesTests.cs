using KillMutants.Coverage;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

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
