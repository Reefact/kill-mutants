namespace KillMutants.Reporting;

/// <summary>Where a run has got to.</summary>
/// <param name="Phase">The stage being worked on.</param>
/// <param name="Completed">How many units of that stage are done.</param>
/// <param name="Total">How many there are in total, or zero when it is not yet known.</param>
/// <param name="Subject">What is being worked on - a project name, usually - or null.</param>
/// <remarks>
/// A run can take minutes. Reporting only at the end leaves the user unable to tell a long run from
/// a hung one, which is the difference between waiting and reaching for Ctrl-C.
/// </remarks>
public sealed record MutationTestProgress(
    MutationTestPhase Phase,
    int Completed = 0,
    int Total = 0,
    string? Subject = null)
{
    /// <summary>True when the phase reports a count rather than just a stage.</summary>
    public bool IsCounted => Total > 0;
}
