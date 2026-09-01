using KillMutants.Coverage;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Testing;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Coverage;

/// <summary>
/// The rule these tests exist for: a measurement that failed must never read as "no test reaches
/// this". That chain - measurement impossible, no hits, NoCoverage, never executed, reported
/// undetected - is a verdict nothing ever tested, and it is silent.
/// </summary>
public class CoverageMapTests
{
    private const string Source = "class C { bool M(int a) => a >= 18; }";

    private static readonly TestName First = TestName.Create("N.C.First");
    private static readonly TestName Second = TestName.Create("N.C.Second");

    [Fact]
    public void A_test_that_reached_a_site_is_the_one_selected_for_its_mutants()
    {
        (IReadOnlyList<Mutant> mutants, MutationSites sites) = Analyse(Source);
        MutantId site = sites.RepresentativeOf[mutants[0].Id];

        CoverageMap map = CoverageMap.From(
            [new CoverageObservation(First, [site]), new CoverageObservation(Second, [])], sites);

        Assert.Equal([First], map.TestsReaching(mutants[0].Id));
    }

    /// <summary>
    /// Every mutant at one expression shares its answer: they replace the same node, so whatever
    /// reaches one reaches all of them.
    /// </summary>
    [Fact]
    public void Mutants_sharing_a_site_share_its_tests()
    {
        (IReadOnlyList<Mutant> mutants, MutationSites sites) = Analyse(Source);
        MutantId site = sites.RepresentativeOf[mutants[0].Id];

        CoverageMap map = CoverageMap.From([new CoverageObservation(First, [site])], sites);

        Assert.True(mutants.Count > 1);
        Assert.All(mutants, mutant => Assert.Equal([First], map.TestsReaching(mutant.Id)));
    }

    [Fact]
    public void A_site_nothing_reached_has_no_tests_when_every_measurement_succeeded()
    {
        (IReadOnlyList<Mutant> mutants, MutationSites sites) = Analyse(Source);

        CoverageMap map = CoverageMap.From([new CoverageObservation(First, [])], sites);

        Assert.Empty(map.TestsReaching(mutants[0].Id)!);
    }

    /// <summary>
    /// The regression test for the whole class of silent false verdicts. A test whose measurement
    /// failed - it timed out, crashed, matched no filter, failed, or left a truncated file - used to
    /// contribute an empty hit list, which is indistinguishable from reaching nothing. The mutant
    /// was then recorded NoCoverage and never run.
    /// </summary>
    [Fact]
    public void A_failed_measurement_makes_its_test_a_candidate_for_every_mutant()
    {
        (IReadOnlyList<Mutant> mutants, MutationSites sites) = Analyse(Source);

        CoverageMap map = CoverageMap.From([new CoverageObservation(First, Reached: null)], sites);

        Assert.All(mutants, mutant => Assert.Equal([First], map.TestsReaching(mutant.Id)));
    }

    [Fact]
    public void A_failed_measurement_is_added_to_the_tests_that_did_reach_a_site()
    {
        (IReadOnlyList<Mutant> mutants, MutationSites sites) = Analyse(Source);
        MutantId site = sites.RepresentativeOf[mutants[0].Id];

        CoverageMap map = CoverageMap.From(
            [new CoverageObservation(First, [site]), new CoverageObservation(Second, Reached: null)],
            sites);

        // Second might also reach the site and might be the only test able to kill the mutant, so
        // narrowing to First alone would be a guess rather than a measurement.
        Assert.Equal([First, Second], map.TestsReaching(mutants[0].Id));
    }

    private static (IReadOnlyList<Mutant> Mutants, MutationSites Sites) Analyse(string source)
    {
        Compilation compilation = TestCompilation.From(source);
        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default).Generate(compilation);

        return (mutants, MutationSites.From(mutants, compilation));
    }
}
