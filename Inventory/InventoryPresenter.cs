using System;
using R3;
using ObservableCollections;
using System.Collections.Generic;

public class InventoryPresenter : IDisposable
{
    private readonly IInventory model;
    private readonly InventoryView view;
    private readonly InventoryUISlotFactory slotFactory;
    private readonly CompositeDisposable subscriptions = new();
    private readonly Dictionary<IReadOnlyInventorySlot, InventorySlotPresenter> presentersMap = new();

    public InventoryPresenter(IInventory inventory, InventoryView inventoryView, InventoryUISlotFactory slotFactory)
    {
        model = inventory;
        view = inventoryView;
        this.slotFactory = slotFactory;

        model.Slots.ObserveAdd().Subscribe(addEvent =>
        {
            IReadOnlyInventorySlot slot = addEvent.Value;
            InventorySlotPresenter presenter = new(slotFactory.CreateSlot(inventoryView.ItemRoot), slot);
            presenter.Removed += Remove;
            presentersMap.Add(slot, presenter);
        }).AddTo(subscriptions);

        model.Slots.ObserveRemove().Subscribe(removeEvent =>
        {
            IReadOnlyInventorySlot slot = removeEvent.Value;
            InventorySlotPresenter presenter = presentersMap[slot];
            presentersMap.Remove(slot);
            presenter.Removed -= Remove;
            presenter.Dispose();
        }).AddTo(subscriptions);
    }

    public void Show() => view.Show();
    public void Hide() => view.Hide();

    public void Dispose()
    {
        foreach (var slot in presentersMap.Values)
            slot.Dispose();

        presentersMap.Clear();
        subscriptions.Dispose();
    }

    private void Remove(IReadOnlyInventorySlot slot, int amount)
    {
        model.TryRemove(slot.Id, amount);
        //TODO current aquarium add
    }
}
