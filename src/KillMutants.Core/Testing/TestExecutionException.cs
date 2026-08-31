namespace KillMutants.Testing;

/// <summary>
/// The test application could not be run, or produced output KillMutants could not read. This
/// reports a broken environment, never an ordinary mutation outcome.
/// </summary>
public sealed class TestExecutionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TestExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public TestExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public TestExecutionException()
    {
    }
}
