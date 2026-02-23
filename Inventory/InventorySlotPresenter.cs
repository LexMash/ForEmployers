using System;
using R3;

public class InventorySlotPresenter : IDisposable
{
    private readonly InventorySlotView view;
    private readonly IReadOnlyInventorySlot slot;
    private readonly IDisposable subscription;

    public event Action<IReadOnlyInventorySlot, int>? Removed;

    public InventorySlotPresenter(InventorySlotView view, IReadOnlyInventorySlot slot)
    {
        this.view = view;
        this.slot = slot;

        view.Title = slot.Id.Value;

        subscription = slot.Amount
            .Where(value => value > 0)
            .Subscribe(value =>
            {
                view.Amount = value.ToString();
                view.AddWidget.Setup(value);
            });

        view.AddWidget.AddPerformed += AddPerformed;
        view.ItemRemoved += ItemRemoved;
    }

    public void Dispose()
    {
        view.AddWidget.AddPerformed -= AddPerformed;
        view.ItemRemoved -= ItemRemoved;

        view.Dispose();
        subscription.Dispose();
    }

    private void AddPerformed(int value)
    {
        Removed?.Invoke(slot, value);
    }

    private void ItemRemoved()
    {
        AddPerformed(1);
    }
}

