using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>What testing one mutant established.</summary>
/// <param name="Mutant">The mutant that was tested.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Detail">Why, when the status needs explaining - compiler errors, for instance.</param>
public sealed record MutantResult(Mutant Mutant, MutantStatus Status, string? Detail = null);
