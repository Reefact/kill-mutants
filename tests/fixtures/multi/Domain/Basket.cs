using Core;

namespace Domain;

/// <summary>Mutable code that also pulls Core into Domain.Tests' reach.</summary>
public static class Basket
{
    public static int Total(int unitPrice, int quantity)
    {
        return unitPrice * quantity;
    }

    public static bool CanAfford(int unitPrice, int quantity, int budget)
    {
        return Money.IsAffordable(Total(unitPrice, quantity), budget);
    }
}
