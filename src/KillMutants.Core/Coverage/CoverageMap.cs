using KillMutants.Mutations;
using KillMutants.Testing;

namespace KillMutants.Coverage;

/// <summary>Which tests reach which mutants.</summary>
/// <remarks>
/// Keyed on <see cref="TestName"/> rather than on the runner's unique ids, which differ between two
/// identical copies of an output directory and so could not survive a sandboxed run. See DEC0006.
/// </remarks>
internal sealed class CoverageMap
{
    private readonly Dictionary<MutantId, List<TestName>> _testsByMutant;

    private CoverageMap(Dictionary<MutantId, List<TestName>> testsByMutant) => _testsByMutant = testsByMutant;

    /// <summary>Builds a map from what each test was observed to reach.</summary>
    /// <remarks>
    /// <para>
    /// An observation with no answer at all - see <see cref="CoverageObservation"/> - is not evidence
    /// that its test reaches nothing, so that test is added to <em>every</em> mutant's candidates. It
    /// might reach any of them, and the only safe reading of "we do not know" is to run it. That
    /// costs time on a run where measurement went wrong, and costs nothing on one where it did not.
    /// </para>
    /// <para>
    /// It also means a site with no recorded hits is only reported uncovered when every measurement
    /// succeeded. Otherwise it inherits the unmeasured tests and gets run like any other.
    /// </para>
    /// </remarks>
    public static CoverageMap From(
        IEnumerable<CoverageObservation> observations,
        MutationSites sites)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sites);

        Dictionary<MutantId, List<TestName>> testsBySite = [];
        List<TestName> unmeasured = [];

        foreach (CoverageObservation observation in observations)
        {
            if (observation.Reached is not { } reached)
            {
                unmeasured.Add(observation.Test);

                continue;
            }

            foreach (MutantId site in reached)
            {
                if (!testsBySite.TryGetValue(site, out List<TestName>? tests))
                {
                    testsBySite[site] = tests = [];
                }

                tests.Add(observation.Test);
            }
        }

        // A site's tests belong to every mutant at that site: they replace the same expression, so
        // whatever reaches one reaches all of them.
        Dictionary<MutantId, List<TestName>> testsByMutant = [];

        foreach ((MutantId mutant, MutantId representative) in sites.RepresentativeOf)
        {
            // A site that could carry no recorder has no answer here, and must not be given the
            // empty one: that would read as "no test reaches this" and report NoCoverage against
            // code the tests may well exercise.
            if (sites.Unmeasurable.Contains(representative))
            {
                continue;
            }

            testsByMutant[mutant] = testsBySite.TryGetValue(representative, out List<TestName>? tests)
                ? [.. tests, .. unmeasured]
                : [.. unmeasured];
        }

        return new CoverageMap(testsByMutant);
    }

    /// <summary>
    /// The tests that reach <paramref name="mutant"/>: an empty list when none does, and
    /// <see langword="null"/> when its site could not be measured at all.
    /// </summary>
    /// <remarks>
    /// The three answers are deliberately distinct. An empty list is a real answer, not a missing
    /// one: no test executes that code, so running the suite could only ever report the mutant as
    /// survived, and <see cref="MutantStatus.NoCoverage"/> says something true about the tests
    /// instead. <see langword="null"/> is the missing answer - the site carries no recorder, see
    /// <see cref="MutationSites"/> - and the only safe reading of it is to run every test.
    /// </remarks>
    public IReadOnlyList<TestName>? TestsReaching(MutantId mutant) =>
        _testsByMutant.TryGetValue(mutant, out List<TestName>? tests) ? tests : null;
}
