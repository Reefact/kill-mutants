using Xunit.v3;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Puts a test in the end-to-end smoke tier: the part of this suite a pull request runs before it can
/// merge. Everything else in the suite runs after the merge, on every platform, and before any
/// release (see <c>.github/workflows/ci.yml</c>).
/// </summary>
/// <remarks>
/// <para>
/// The tier is opt-in, and that direction is the point. A test added without this attribute lands in
/// the full tier, which still runs it on all three platforms on every push to <c>main</c>: forgetting
/// the attribute costs a later answer, never a missing one. The other direction - a <c>Slow</c> marker
/// that a forgotten attribute leaves out - would let every new sweep inflate the pull-request
/// budget silently, until the fast gate was as slow as the one it replaced.
/// </para>
/// <para>
/// What earns a place is measured, not named: a test joins because a timed run showed it cheap
/// <em>and</em> because it is the only thing in the tier that reaches some mechanism - the process
/// launch of the real command line, a real MSBuild and Roslyn emit, a test application started per
/// mutant, the filesystem a path is compared on. A class that never leaves the process (settings
/// resolution, report rendering) carries the attribute as a whole; a class whose tests each run a
/// sweep carries it test by test, and only where the sweep is short.
/// </para>
/// <para>
/// This is not a quarantine. Nothing here skips, weakens or reorders an assertion: the smoke tier
/// is a selection of tests that also run, unchanged, in the full one.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SmokeAttribute : Attribute, ITraitAttribute
{
    /// <summary>The trait name the workflows filter on.</summary>
    public const string TraitName = "Tier";

    /// <summary>The trait value the workflows filter on.</summary>
    public const string TraitValue = "Smoke";

    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TraitName, TraitValue)];
}
