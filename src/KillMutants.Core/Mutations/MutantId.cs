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

    /// <inheritdoc />
    public override string ToString() => $"M{_value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
