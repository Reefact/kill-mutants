using KillMutants.Mutations;
using KillMutants.Testing;

namespace KillMutants.Coverage;

/// <summary>What running one test on its own established about the sites it reaches.</summary>
/// <param name="Test">The test that was run.</param>
/// <param name="Reached">
/// The sites it reached, or <see langword="null"/> when the measurement failed and nothing is known.
/// </param>
/// <remarks>
/// A record rather than a tuple because the difference between an empty list and
/// <see langword="null"/> is the whole point, and a bare <c>IReadOnlyList&lt;MutantId&gt;?</c> passed
/// around in a tuple invites exactly the collapse this type exists to prevent: an unknown
/// measurement read as "this test reaches nothing", which turns into a mutant that is never run and
/// is then reported as undetected.
/// </remarks>
internal sealed record CoverageObservation(TestName Test, IReadOnlyList<MutantId>? Reached);
