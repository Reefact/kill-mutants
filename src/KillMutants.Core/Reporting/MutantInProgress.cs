using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>A mutant a worker has started or finished testing, as a progress reader names it.</summary>
/// <param name="Id">The counter this run gave it, as the report and the plan name it.</param>
/// <param name="Family">The mutator family that proposed it.</param>
/// <param name="Path">The source file, relative to the directory the run was pointed at.</param>
/// <param name="Line">One-based line.</param>
/// <param name="Status">What became of it, or null while it is still being tested.</param>
/// <param name="Budget">The time it was given before it counts as timed out, when known.</param>
/// <remarks>
/// Two events per mutant rather than one, because the question a reader of a stalled run asks is
/// about the mutant that started and has not finished: only a start says which one that is, and how
/// long it has been running is measured from it (#65).
/// </remarks>
public sealed record MutantInProgress(
    string Id,
    string Family,
    string Path,
    int Line,
    MutantStatus? Status = null,
    TimeSpan? Budget = null)
{
    /// <summary>True when this reports the end of the mutant's test.</summary>
    public bool IsFinished => Status is not null;

    /// <summary>Describes <paramref name="mutant"/> as started, with the budget it was given.</summary>
    public static MutantInProgress Started(Mutant mutant, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(mutant);

        return new MutantInProgress(
            mutant.Id.ToString(), mutant.Mutator.ToString(), mutant.RelativePath, mutant.Location.Line, Budget: budget);
    }
}
