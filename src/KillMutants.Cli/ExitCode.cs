namespace KillMutants.Cli;

/// <summary>
/// What KillMutants tells the shell.
/// </summary>
/// <remarks>
/// A public contract: scripts and CI jobs will encode these, so they are chosen once and not
/// renumbered. The distinction that earns its keep is between <see cref="ScoreBelowThreshold"/> and
/// <see cref="CouldNotRun"/> - "your tests are weaker than you asked for" and "this tool did not
/// work" call for different reactions, and a job that cannot tell them apart will eventually treat a
/// broken environment as a quality regression, or worse, the other way round.
/// </remarks>
internal static class ExitCode
{
    /// <summary>The run finished, and the score met the threshold if one was set.</summary>
    public const int Success = 0;

    /// <summary>The run finished, but the mutation score is below the requested threshold.</summary>
    public const int ScoreBelowThreshold = 1;

    /// <summary>KillMutants could not complete the run.</summary>
    public const int CouldNotRun = 2;

    /// <summary>The command line was not understood.</summary>
    public const int BadUsage = 64;
}
