using UnityEngine;

public class InventoryView : MonoBehaviour
{
    [field: SerializeField] public RectTransform ItemRoot = default!;

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
