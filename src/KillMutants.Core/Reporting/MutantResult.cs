using KillMutants.Mutations;
using KillMutants.Testing;

namespace KillMutants.Reporting;

/// <summary>What testing one mutant established.</summary>
/// <param name="Mutant">The mutant that was tested.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Detail">Why, when the status needs explaining - compiler errors, for instance.</param>
/// <param name="KilledBy">
/// The tests that failed against this mutant, empty unless it was killed by one.
/// </param>
/// <param name="Disagreement">
/// Why this verdict is not to be trusted, when testing the mutant again did not reproduce it.
/// </param>
/// <param name="Overturned">
/// How an earlier verdict for this mutant was corrected, when re-running it alone reached a
/// different one that is known to be the better of the two.
/// </param>
public sealed record MutantResult(
    Mutant Mutant,
    MutantStatus Status,
    string? Detail = null,
    IReadOnlyList<TestName>? KilledBy = null,
    string? Disagreement = null,
    string? Overturned = null)
{
    /// <summary>
    /// Set when this mutant's first verdict was replaced by a re-run whose conditions were better,
    /// and null when it was not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a <see cref="Disagreement"/>, which says the opposite thing. A disagreement
    /// is two runs of equal standing reaching different answers, and this tool refusing to choose
    /// between them. This is a correction: the first verdict was reached while workers competed for
    /// the machine, the second alone, and the second is the one to believe.
    /// </para>
    /// <para>
    /// It is recorded because the correction is itself a measurement - of the time budget, not of
    /// the mutant. A run that overturns many timeouts is a run whose budget is too tight for its
    /// concurrency, and without this the only trace of that is a number quietly getting better.
    /// </para>
    /// </remarks>
    public string? Overturned { get; init; } = Overturned;

    /// <summary>
    /// Set when a re-run of this mutant reached a different verdict, and null when it did not or was
    /// never attempted.
    /// </summary>
    /// <remarks>
    /// Recorded beside the status rather than replacing it. Which of the two runs told the truth is
    /// not something this tool can decide, and quietly picking one would be the exact failure the
    /// re-run exists to catch.
    /// </remarks>
    public string? Disagreement { get; init; } = Disagreement;

    /// <summary>The tests that failed against this mutant, named so they can be run again.</summary>
    /// <remarks>
    /// <para>
    /// A kill nobody can reproduce is not a kill. With this, the mutation and the failing test, a
    /// reader can put the change into the file by hand, run those tests, and watch them fail -
    /// without this tool in the loop at all. That is the only way a disputed verdict can be settled.
    /// </para>
    /// <para>
    /// Testing stops at the first failure, so this names the test that settled it rather than every
    /// test that would have failed. One is enough to reproduce the kill, and looking for the rest
    /// would cost a full suite run per mutant.
    /// </para>
    /// </remarks>
    public IReadOnlyList<TestName> KilledBy { get; init; } = KilledBy ?? [];

    /// <summary>What this result says about the test suite - see <see cref="MutantOutcome"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The status is not one this tool produces.</exception>
    public MutantOutcome Outcome => Status switch
    {
        // A mutation that hangs the suite changed observable behaviour, and the suite is what
        // noticed. Counting it as a survivor would reward a mutation for being worse.
        MutantStatus.Killed or MutantStatus.Timeout => MutantOutcome.Detected,

        // Uncovered code is the clearest case of all: no test would have failed.
        MutantStatus.Survived or MutantStatus.NoCoverage => MutantOutcome.Undetected,

        MutantStatus.CompileError => MutantOutcome.Untestable,

        _ => throw new ArgumentOutOfRangeException(
            nameof(Status), Status, "No outcome is defined for this status."),
    };
}
