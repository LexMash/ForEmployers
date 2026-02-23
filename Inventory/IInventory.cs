using ObservableCollections;

public interface IInventory
{
    IReadOnlyObservableList<IReadOnlyInventorySlot> Slots { get; }

    bool TryRemove(Id id, int amount);
    bool TryAdd(Id id, int amount);
}