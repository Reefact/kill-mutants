namespace KillMutants.Selection;

/// <summary>
/// A partial run could not be set up: the change could not be read, or the base revision's project
/// graph could not be resolved.
/// </summary>
/// <remarks>
/// Always a refusal to start, never a finding. A partial run rests on two revisions being readable,
/// and ADR-0010 is explicit that when the base side cannot be resolved the run is not to be trusted
/// to HEAD alone. Falling back to HEAD would produce a green run for exactly the reason the base
/// side exists, so the run stops instead and says what to fix.
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
