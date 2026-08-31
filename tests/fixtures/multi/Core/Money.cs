namespace Core;

/// <summary>Mutable code reached by two different test suites.</summary>
public static class Money
{
    public static bool IsAffordable(int price, int budget)
    {
        return price <= budget;
    }
}
