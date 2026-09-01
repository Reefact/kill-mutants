namespace KillMutants.Mutations;

/// <summary>Identifies a mutant within a single mutation test run.</summary>
public readonly record struct MutantId
{
    private readonly int _value;

    private MutantId(int value) => _value = value;

    /// <summary>The first identifier handed out in a run.</summary>
    public static MutantId First => new(1);

    /// <summary>The identifier following this one.</summary>
    public MutantId Next() => new(_value + 1);

    /// <summary>
    /// The bare number, for the few places that must carry an identifier across a process boundary -
    /// the coverage probe compiled into the assembly under test writes these to a file.
    /// </summary>
    internal int Value => _value;

    /// <summary>Rebuilds an identifier from a number written by the coverage probe.</summary>
    internal static MutantId FromValue(int value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"M{_value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
