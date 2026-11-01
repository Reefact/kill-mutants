namespace KillMutants.Reporting;

/// <summary>Where a run has got to.</summary>
/// <param name="Phase">The stage being worked on.</param>
/// <param name="Completed">How many units of that stage are done.</param>
/// <param name="Total">How many there are in total, or zero when it is not yet known.</param>
/// <param name="Subject">What is being worked on - a project name, usually - or null.</param>
/// <param name="Mutant">
/// The mutant a worker has just started or just finished testing, or null. Added for #65: a counter
/// that stops moving cannot tell a slow mutant from a stuck run, and naming the mutant in progress is
/// what turns it into a diagnosis.
/// </param>
/// <param name="SetAside">
/// A part of the run set aside just now (DEC0022), or null. Carried here so that a reader of the
/// console learns it at the minute it happens rather than in the final report (#65).
/// </param>
/// <remarks>
/// A run can take minutes. Reporting only at the end leaves the user unable to tell a long run from
/// a hung one, which is the difference between waiting and reaching for Ctrl-C.
/// </remarks>
public sealed record MutationTestProgress(
    MutationTestPhase Phase,
    int Completed = 0,
    int Total = 0,
    string? Subject = null,
    MutantInProgress? Mutant = null,
    SetAside? SetAside = null)
{
    /// <summary>True when the phase reports a count rather than just a stage.</summary>
    public bool IsCounted => Total > 0;
}
