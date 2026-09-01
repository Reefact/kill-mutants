namespace KillMutants.Coverage;

/// <summary>
/// Coverage could not be measured. Reported rather than worked around, because silently losing test
/// selection would turn a fast run into a slow one with no explanation.
/// </summary>
public sealed class CoverageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CoverageException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public CoverageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public CoverageException()
    {
    }
}
