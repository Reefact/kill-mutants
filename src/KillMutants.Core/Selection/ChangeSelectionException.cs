namespace KillMutants.Selection;

/// <summary>
/// A partial run could not be set up: the change could not be read, or the earlier state's project
/// graph could not be resolved.
/// </summary>
/// <remarks>
/// Always a refusal to start, never a finding. A partial run rests on both states being readable,
/// and DEC0011 is explicit that when the earlier side cannot be resolved the run is not to be
/// trusted on the current code alone. Falling back to it would produce a green run for exactly the
/// reason the earlier side exists, so the run stops instead and says what to fix.
/// </remarks>
public sealed class ChangeSelectionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ChangeSelectionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ChangeSelectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ChangeSelectionException()
    {
    }
}
