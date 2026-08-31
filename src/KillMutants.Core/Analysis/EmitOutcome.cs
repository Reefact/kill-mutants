namespace KillMutants.Analysis;

/// <summary>The result of emitting an assembly: the bytes, or why they could not be produced.</summary>
/// <remarks>
/// A mutation that does not compile is an expected, ordinary outcome, so it is modelled as a value
/// rather than raised as an exception.
/// </remarks>
internal sealed class EmitOutcome
{
    private EmitOutcome(byte[]? assembly, string? diagnostics)
    {
        Assembly = assembly;
        Diagnostics = diagnostics;
    }

    /// <summary>The emitted assembly, when emission succeeded.</summary>
    public byte[]? Assembly { get; }

    /// <summary>The compiler errors, when emission failed.</summary>
    public string? Diagnostics { get; }

    /// <summary>True when the assembly was produced.</summary>
    public bool Success => Assembly is not null;

    public static EmitOutcome Succeeded(byte[] assembly) => new(assembly, null);

    public static EmitOutcome Failed(string diagnostics) => new(null, diagnostics);
}
