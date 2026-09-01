namespace Sample.Library;

/// <summary>Depends on a type the generator contributes, so the project cannot compile without it.</summary>
public static class Ages
{
    public static bool IsAdult(int age)
    {
        return age >= Limits.AdultAge;
    }
}
