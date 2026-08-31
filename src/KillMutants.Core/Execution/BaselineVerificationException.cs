namespace KillMutants.Execution;

/// <summary>
/// The unmutated code did not pass its own tests, so no mutation result would be meaningful.
/// </summary>
public sealed class BaselineVerificationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BaselineVerificationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public BaselineVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public BaselineVerificationException()
    {
    }
}
