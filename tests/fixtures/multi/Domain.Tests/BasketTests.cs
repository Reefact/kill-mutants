using Domain;

namespace Domain.Tests;

public class BasketTests
{
    [Fact]
    public void The_total_is_the_unit_price_times_the_quantity()
    {
        Assert.Equal(12, Basket.Total(3, 4));
    }

    [Theory]
    [InlineData(3, 4, 12, true)]
    [InlineData(3, 4, 11, false)]
    public void A_basket_within_budget_can_be_afforded(int unitPrice, int quantity, int budget, bool expected)
    {
        Assert.Equal(expected, Basket.CanAfford(unitPrice, quantity, budget));
    }
}
