namespace Sample.Support;

/// <summary>An assertion helper, which is why this project references xUnit at all.</summary>
public static class Affordability
{
    public static void AssertAffordable(int price, int budget) =>
        Assert.True(Sample.Library.Money.IsAffordable(price, budget));
}
