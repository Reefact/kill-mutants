namespace KillMutants.Testing;

/// <summary>
/// A test's stable identity: its fully qualified <c>Namespace.Class.Method</c>.
/// </summary>
/// <remarks>
/// Deliberately not the runner's unique id, which is derived from the test assembly's path and so
/// differs between two identical copies of an output directory. See ADR-0006.
/// </remarks>
public readonly record struct TestName
{
    private readonly string? _value;

    private TestName(string value) => _value = value;

    /// <summary>Creates a test name.</summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static TestName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A test name must not be blank.", nameof(value));
        }

        return new TestName(value);
    }

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;
}
