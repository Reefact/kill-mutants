using Sample.Support;

namespace Sample.Library.Tests;

public class MoneyTests
{
    [Fact]
    public void A_price_within_budget_is_affordable() => Affordability.AssertAffordable(5, 10);

    [Fact]
    public void A_price_above_budget_is_not() =>
        Assert.False(Money.IsAffordable(price: 11, budget: 10));
}
