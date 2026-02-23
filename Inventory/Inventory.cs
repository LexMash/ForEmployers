using ObservableCollections;
using R3;
using System;
using System.Collections.Generic;

public class Inventory : IInventory, IDisposable
{
    private readonly ObservableDictionary<Id, ObservableInventorySlot> slotsMap = new();
    private readonly ObservableList<IReadOnlyInventorySlot> readOnlySlots = new();
    private readonly CompositeDisposable subscriptions = new();

    public Inventory(List<InventorySlot> originData)
    {
        foreach (var slot in originData)
        {
            var observableSlot = new ObservableInventorySlot(slot);
            slotsMap.Add(slot.Id, observableSlot);
            readOnlySlots.Add(observableSlot);
        }

        slotsMap.ObserveAdd().Subscribe(addEvent =>
        {
            ObservableInventorySlot obsSlot = addEvent.Value.Value;
            originData.Add(obsSlot.Origin);
            readOnlySlots.Add(obsSlot);
        }).AddTo(subscriptions);

        slotsMap.ObserveRemove().Subscribe(removeEvent =>
        {
            ObservableInventorySlot obsSlot = removeEvent.Value.Value;
            originData.Remove(obsSlot.Origin);
            readOnlySlots.Remove(obsSlot);
        }).AddTo(subscriptions);
    }

    public IReadOnlyObservableList<IReadOnlyInventorySlot> Slots => readOnlySlots;

    public bool TryAdd(Id id, int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        if (slotsMap.TryGetValue(id, out ObservableInventorySlot? existingSlot))
            existingSlot.Amount.Value += amount;
        else
            slotsMap.Add(id, new ObservableInventorySlot(new InventorySlot(id, amount)));

        return true;
    }

    public bool TryRemove(Id id, int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        if (slotsMap.TryGetValue(id, out ObservableInventorySlot? existingSlot))
        {
            int result = existingSlot.Amount.Value - amount;

            if (result < 0)
                return false;

            existingSlot.Amount.Value = result;

            if (result == 0)
                slotsMap.Remove(id);

            return true;
        }
        else
            throw new ArgumentException(nameof(id));
    }

    public void Dispose()
    {
        foreach (var kvp in slotsMap)
            kvp.Value.Dispose();

        subscriptions.Dispose();
    }
}