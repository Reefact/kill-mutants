namespace Sample.Library;

/// <summary>Fixture code under mutation. Deliberately tiny.</summary>
public static class Ages
{
    /// <summary>The single mutation site milestone 1 targets: <c>&gt;=</c> becomes <c>&gt;</c>.</summary>
    public static bool IsAdult(int age)
    {
        return age >= 18;
    }
}
