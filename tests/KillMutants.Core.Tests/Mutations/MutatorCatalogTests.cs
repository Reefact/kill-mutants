using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// The families do not carry equal signal, and the difference is measurable: against this
/// repository the operator families detect 45% to 55% of what they produce, while StringLiteral and
/// BooleanLiteral together make half the mutants and detect 10% to 15%. Those are true findings, and
/// valuable on some projects, which is why this is a choice rather than a deletion.
/// </summary>
public class MutatorCatalogTests
{
    private static readonly MutatorName Comparison = MutatorName.Create("Comparison");
    private static readonly MutatorName StringLiteral = MutatorName.Create("StringLiteral");

    [Fact]
    public void The_default_catalog_runs_every_family()
    {
        Assert.Equal(MutatorCatalog.Names, MutatorCatalog.Default.Mutators.Select(m => m.Name));
        Assert.Equal(MutatorCatalog.Names, MutatorCatalog.Of().Mutators.Select(m => m.Name));
    }

    [Fact]
    public void Naming_families_runs_only_those()
    {
        MutatorCatalog catalog = MutatorCatalog.Of([Comparison, StringLiteral]);

        Assert.Equal([Comparison, StringLiteral], catalog.Mutators.Select(m => m.Name));
    }

    [Fact]
    public void Dropping_a_family_leaves_the_rest()
    {
        MutatorCatalog catalog = MutatorCatalog.Of(unwanted: [StringLiteral]);

        Assert.DoesNotContain(StringLiteral, catalog.Mutators.Select(m => m.Name));
        Assert.Equal(MutatorCatalog.Names.Count - 1, catalog.Mutators.Count);
    }

    /// <summary>Dropping is applied second, so the two options compose without an ordering rule.</summary>
    [Fact]
    public void Dropping_wins_over_naming()
    {
        MutatorCatalog catalog = MutatorCatalog.Of([Comparison, StringLiteral], [StringLiteral]);

        Assert.Equal([Comparison], catalog.Mutators.Select(m => m.Name));
    }

    /// <summary>
    /// A typo must not silently narrow a run: a catalogue quietly missing the family the user meant
    /// to keep would report a score for something they did not ask for.
    /// </summary>
    [Theory]
    [InlineData("Comparisons")]
    [InlineData("comparison")]
    [InlineData("Linq")]
    public void An_unknown_family_is_rejected_with_the_list_of_real_ones(string name)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MutatorCatalog.Of([MutatorName.Create(name)]));

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        Assert.Contains("Comparison", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_family_is_rejected_when_dropping_too()
    {
        Assert.Throws<ArgumentException>(() => MutatorCatalog.Of(unwanted: [MutatorName.Create("Nope")]));
    }

    /// <summary>Narrowing to nothing is a legitimate, if pointless, request rather than a crash.</summary>
    [Fact]
    public void Dropping_every_family_leaves_an_empty_catalog()
    {
        Assert.Empty(MutatorCatalog.Of(unwanted: MutatorCatalog.Names).Mutators);
    }

    [Fact]
    public void The_names_are_the_ones_the_public_surface_offers()
    {
        Assert.Equal(MutatorCatalog.Names, MutationTesting.MutatorFamilies);
    }
}
