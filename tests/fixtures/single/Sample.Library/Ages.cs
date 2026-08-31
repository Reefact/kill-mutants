namespace Sample.Library;

/// <summary>Fixture code under mutation. Small, but it exercises every mutator family.</summary>
public static class Ages
{
    /// <summary>The mutation milestone 1 was built around: <c>&gt;=</c> becomes <c>&gt;</c>.</summary>
    public static bool IsAdult(int age)
    {
        return age >= 18;
    }

    /// <summary>Comparison and logical operators together.</summary>
    public static bool IsEligible(int age, bool hasConsent)
    {
        return age >= 18 && hasConsent;
    }

    /// <summary>Negation.</summary>
    public static bool IsMinor(int age)
    {
        return !IsAdult(age);
    }

    /// <summary>Arithmetic.</summary>
    public static int TotalPrice(int unitPrice, int quantity)
    {
        return unitPrice * quantity;
    }

    /// <summary>String literals.</summary>
    public static string Describe(int age)
    {
        return IsAdult(age) ? "adult" : "minor";
    }
}
