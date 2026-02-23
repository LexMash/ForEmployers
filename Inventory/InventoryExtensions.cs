using System.Collections.Generic;

public static class InventoryExtensions
{
    public static void Add(this Inventory inventory, IEnumerable<(Id id, int amount)> pack)
    {
        foreach ((Id id, int amount) item in pack)
            inventory.TryAdd(item.id, item.amount);
    }
}

