namespace Sample.Library;

/// <summary>Deliberately small: this fixture exists to be netstandard2.0 under a net10.0 suite, not to be thorough.</summary>
public static class Ages
{
    /// <summary>One comparison, so the run has exactly one family to work with.</summary>
    public static bool IsAdult(int age)
    {
        return age >= 18;
    }
}
