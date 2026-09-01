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

    /// <summary>String literals, and the conditional whose branches can be swapped.</summary>
    public static string Describe(int age)
    {
        return IsAdult(age) ? "adult" : "minor";
    }

    /// <summary>Bitwise operators.</summary>
    public static int CommonFlags(int left, int right)
    {
        return left & right;
    }

    /// <summary>Boolean literals.</summary>
    public static bool RequiresGuardian(int age)
    {
        if (IsAdult(age))
        {
            return false;
        }

        return true;
    }

    /// <summary>Increment.</summary>
    public static int AgeOnNextBirthday(int age)
    {
        age++;

        return age;
    }

    /// <summary>Compound assignment.</summary>
    public static int TotalOfAges(int[] ages)
    {
        int total = 0;

        foreach (int age in ages)
        {
            total += age;
        }

        return total;
    }

    /// <summary>Null-coalescing.</summary>
    public static string NameOrDefault(string? name)
    {
        return name ?? "unknown";
    }
}
