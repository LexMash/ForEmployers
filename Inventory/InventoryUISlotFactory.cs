using UnityEngine;

public class InventoryUISlotFactory
{
    private readonly InventorySlotView viewPrefab;

    public InventoryUISlotFactory(InventorySlotView viewPrefab)
    {
        this.viewPrefab = viewPrefab;
    }

    public InventorySlotView CreateSlot(RectTransform root)
    {
        var view = GameObject.Instantiate(viewPrefab, root);
        view.Released += Release;
        return view;
    }

    public void Release(InventorySlotView view)
    {
        view.Released -= Release;
        GameObject.Destroy(view.gameObject);
    }
}