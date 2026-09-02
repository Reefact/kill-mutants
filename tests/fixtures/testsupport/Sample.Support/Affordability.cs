namespace Sample.Support;

/// <summary>
/// An assertion helper, which is why this project references xUnit at all. It also carries a
/// comparison of its own, so that a run can be seen either mutating it or leaving it alone.
/// </summary>
public static class Affordability
{
    public static void AssertAffordable(int price, int budget) =>
        Assert.True(Sample.Library.Money.IsAffordable(price, budget));

    /// <summary>Scaffolding arithmetic: nobody set out to measure whether this is tested.</summary>
    public static int Cheapest(int first, int second) => first <= second ? first : second;
}
