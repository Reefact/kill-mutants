using Core;

namespace Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, false)]
    [InlineData(4, 5, true)]
    public void A_price_within_budget_is_affordable(int price, int budget, bool expected)
    {
        Assert.Equal(expected, Money.IsAffordable(price, budget));
    }
}
