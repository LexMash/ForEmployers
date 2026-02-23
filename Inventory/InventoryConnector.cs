using R3;
using System.Collections.Generic;
using System;
using ObservableCollections;

public class InventoryConnector : IDisposable
{
    private readonly Dictionary<IReadOnlyInventorySlot, IDisposable> slotSubscribtions = new();
    private readonly CompositeDisposable subscriptions = new();

    private readonly IInventory first;
    private readonly IInventory second;

    public InventoryConnector(IInventory first, IInventory second)
    {
        this.first = first;
        this.second = second;

        foreach (IReadOnlyInventorySlot slot in first.Slots)
            SubscribeOnSlotAmountChanging(first, second, slot);

        foreach (IReadOnlyInventorySlot slot in second.Slots)
            SubscribeOnSlotAmountChanging(second, first, slot);

        first.Slots
            .ObserveAdd()
            .Subscribe(addEvent =>
            {
                IReadOnlyInventorySlot slot = addEvent.Value;
                SubscribeOnSlotAmountChanging(first, second, slot);
            })
            .AddTo(subscriptions);

        first.Slots
            .ObserveRemove()
            .Subscribe(removeEvent => UnsubscribeOnSlot(removeEvent.Value))
            .AddTo(subscriptions);

        second.Slots
            .ObserveAdd()
            .Subscribe(addEvent =>
            {
                IReadOnlyInventorySlot slot = addEvent.Value;
                SubscribeOnSlotAmountChanging(second, first, slot);
            })
            .AddTo(subscriptions);

        second.Slots
            .ObserveRemove()
            .Subscribe(removeEvent => UnsubscribeOnSlot(removeEvent.Value))
            .AddTo(subscriptions);
    }

    public void Dispose()
    {
        UnsubscribeFromExisting();
    }

    private void UnsubscribeOnSlot(IReadOnlyInventorySlot slot)
    {
        IDisposable subscription = slotSubscribtions[slot];
        slotSubscribtions.Remove(slot);
        subscription.Dispose();
    }

    private void SubscribeOnSlotAmountChanging(IInventory from, IInventory to, IReadOnlyInventorySlot slot)
    {
        IDisposable subscription = slot.Amount
            .Pairwise()
            .Where(values => values.Current < values.Previous)
            .Subscribe(values =>
            {
                Id id = slot.Id;
                int amount = values.Previous - values.Current;

                if (to.TryAdd(id, amount) == false)
                    from.TryAdd(id, amount);
            });

        slotSubscribtions.Add(slot, subscription);
    }

    private void UnsubscribeFromExisting()
    {
        subscriptions.Dispose();

        foreach (var subscription in slotSubscribtions.Values)
            subscription.Dispose();

        slotSubscribtions.Clear();
    }
}
