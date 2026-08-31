namespace KillMutants.Mutations;

/// <summary>
/// Names the rule that produced a mutation. A name, not a bare string, so that a mutator name
/// cannot silently be passed where some other piece of text was meant.
/// </summary>
public readonly record struct MutatorName
{
    private readonly string? _value;

    private MutatorName(string value) => _value = value;

    /// <summary>Creates a mutator name.</summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static MutatorName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A mutator name must not be blank.", nameof(value));
        }

        return new MutatorName(value);
    }

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;
}
