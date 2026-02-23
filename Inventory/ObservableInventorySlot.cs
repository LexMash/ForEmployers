using R3;
using System;

public class ObservableInventorySlot : IReadOnlyInventorySlot
{
    private readonly IDisposable subscription;

    public ObservableInventorySlot(InventorySlot slot)
    {
        Origin = slot;
        Id = slot.Id;
        Amount = new ReactiveProperty<int>(slot.Amount);

        subscription = Amount.Subscribe(value => slot.Amount = value);
    }

    public InventorySlot Origin { get; }
    public Id Id { get; }
    public ReactiveProperty<int> Amount { get; }
    ReadOnlyReactiveProperty<int> IReadOnlyInventorySlot.Amount => Amount;

    public void Dispose() => subscription.Dispose();
}