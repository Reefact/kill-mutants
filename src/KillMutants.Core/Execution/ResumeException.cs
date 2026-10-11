namespace KillMutants.Execution;

/// <summary>
/// The run was asked to continue a series it cannot continue honestly, and refused rather than
/// inherit verdicts measured on something else.
/// </summary>
/// <remarks>
/// Raised once the mutants are selected and the half of the identity only the run can know - the
/// population, the project graph, the test framework - turns out to differ from the plan's, although
/// everything known before the run matched. Nothing is emptied on the way out: the log may hold hours
/// of verdicts, and the message names the difference so the user can decide. See DEC0015.
/// </remarks>
public sealed class ResumeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ResumeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ResumeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ResumeException()
    {
    }
}
