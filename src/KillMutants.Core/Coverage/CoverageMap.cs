using KillMutants.Mutations;
using KillMutants.Testing;

namespace KillMutants.Coverage;

/// <summary>Which tests reach which mutants.</summary>
/// <remarks>
/// Keyed on <see cref="TestName"/> rather than on the runner's unique ids, which differ between two
/// identical copies of an output directory and so could not survive a sandboxed run. See ADR-0006.
/// </remarks>
internal sealed class CoverageMap
{
    private readonly Dictionary<MutantId, List<TestName>> _testsByMutant;

    private CoverageMap(Dictionary<MutantId, List<TestName>> testsByMutant) => _testsByMutant = testsByMutant;

    /// <summary>Builds a map from what each test was observed to reach.</summary>
    public static CoverageMap From(
        IEnumerable<(TestName Test, IReadOnlyList<MutantId> Reached)> observations,
        MutationSites sites)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sites);

        Dictionary<MutantId, List<TestName>> testsBySite = [];

        foreach ((TestName test, IReadOnlyList<MutantId> reached) in observations)
        {
            foreach (MutantId site in reached)
            {
                if (!testsBySite.TryGetValue(site, out List<TestName>? tests))
                {
                    testsBySite[site] = tests = [];
                }

                tests.Add(test);
            }
        }

        // A site's tests belong to every mutant at that site: they replace the same expression, so
        // whatever reaches one reaches all of them.
        Dictionary<MutantId, List<TestName>> testsByMutant = [];

        foreach ((MutantId mutant, MutantId representative) in sites.RepresentativeOf)
        {
            testsByMutant[mutant] = testsBySite.TryGetValue(representative, out List<TestName>? tests)
                ? tests
                : [];
        }

        return new CoverageMap(testsByMutant);
    }

    /// <summary>
    /// The tests that reach <paramref name="mutant"/>, or an empty list when nothing does.
    /// </summary>
    /// <remarks>
    /// An empty list is a real answer, not a missing one: no test executes that code, so running the
    /// suite against the mutant could only ever report it as survived. It is recorded as
    /// <see cref="MutantStatus.NoCoverage"/> instead, which says something true about the tests
    /// rather than something misleading about the mutant.
    /// </remarks>
    public IReadOnlyList<TestName> TestsReaching(MutantId mutant) =>
        _testsByMutant.TryGetValue(mutant, out List<TestName>? tests) ? tests : [];
}
