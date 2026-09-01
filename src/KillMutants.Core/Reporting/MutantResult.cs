using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>What testing one mutant established.</summary>
/// <param name="Mutant">The mutant that was tested.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Detail">Why, when the status needs explaining - compiler errors, for instance.</param>
public sealed record MutantResult(Mutant Mutant, MutantStatus Status, string? Detail = null)
{
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
