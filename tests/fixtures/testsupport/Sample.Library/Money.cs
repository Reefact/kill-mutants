namespace Sample.Library;

public static class Money
{
    public static bool IsAffordable(int price, int budget) => price <= budget;
}
