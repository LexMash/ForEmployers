using System;

[Serializable]
public class InventorySlot
{
    public Id Id;
    public int Amount;

    public InventorySlot(Id id, int amount)
    {
        Id = id;
        Amount = amount;
    }
}