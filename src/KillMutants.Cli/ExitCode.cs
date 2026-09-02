namespace KillMutants.Cli;

/// <summary>
/// What KillMutants tells the shell.
/// </summary>
/// <remarks>
/// A public contract: scripts and CI jobs will encode these, so they are chosen once and not
/// renumbered. The distinction that earns its keep is between <see cref="GateNotPassed"/> and
/// <see cref="CouldNotRun"/> - "your tests are weaker than you asked for" and "this tool did not
/// work" call for different reactions, and a job that cannot tell them apart will eventually treat a
/// broken environment as a quality regression, or worse, the other way round.
/// </remarks>
internal static class ExitCode
{
    /// <summary>The run finished, and the gate it was given was passed.</summary>
    public const int Success = 0;

    /// <summary>
    /// The run finished, and the gate it was asked for did not pass. Standard error says which case.
    /// </summary>
    /// <remarks>
    /// Named for the gate rather than for one of its causes, because it already had more than one
    /// before <c>--since</c> existed: a score below the threshold, and a score that is undefined
    /// because nothing could be tested - which cannot be shown to meet a threshold and so must not
    /// report success. DEC0010 adds a third, a partial run whose findings the caller asked to fail
    /// on. All three are "what you asked me to check did not pass", which is what a build script
    /// branches on; the reason is on standard error for whoever reads it. See DEC0009.
    /// </remarks>
    public const int GateNotPassed = 1;

    /// <summary>KillMutants could not complete the run.</summary>
    public const int CouldNotRun = 2;

    /// <summary>The command line was not understood.</summary>
    public const int BadUsage = 64;
}
