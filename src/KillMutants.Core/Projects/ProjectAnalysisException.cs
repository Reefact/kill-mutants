namespace KillMutants.Projects;

/// <summary>
/// A project could not be analysed. This reports an environment or configuration problem the user
/// must fix; it is never used to signal an ordinary mutation outcome.
/// </summary>
public sealed class ProjectAnalysisException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProjectAnalysisException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ProjectAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ProjectAnalysisException()
    {
    }
}
