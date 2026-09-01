using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>What one mutator family cost a run, and what it bought.</summary>
/// <param name="Mutator">The family.</param>
/// <param name="Total">Mutants it produced.</param>
/// <param name="Detected">Of those, the ones the suite noticed.</param>
/// <param name="Undetected">The ones it did not, whether they ran or could not have.</param>
/// <param name="Untestable">The ones KillMutants could not build.</param>
/// <remarks>
/// Reported because the families do not carry equal signal, and the difference is large enough to
/// act on. Measured against this repository: the operator families kill between 45% and 55% of what
/// they produce, while <c>StringLiteral</c> and <c>BooleanLiteral</c> together account for half the
/// mutants and kill 10% to 15% of them - error messages and flags nothing asserts on. Whether that
/// is a finding or noise depends on the project, so KillMutants reports the split and lets
/// <c>--without</c> act on it, rather than deciding on the user's behalf.
/// </remarks>
public sealed record MutatorSummary(
    MutatorName Mutator,
    int Total,
    int Detected,
    int Undetected,
    int Untestable)
{
    /// <summary>The share of this family's judged mutants that the suite detected.</summary>
    public MutationScore Score { get; } = MutationScore.FromCounts(Detected, Undetected);
}
