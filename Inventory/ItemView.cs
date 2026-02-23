using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemView : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    public bool IsDraggable = true; //TODO

    [SerializeField] private Image icon = default!;
    [SerializeField] private RectTransform rectTransform = default!;

    private float scaleFactor;
    private Transform prevParent = default!;
    private Transform root = default!;

    public event Action? StartDrag;
    public event Action<PointerEventData>? EndDrag;

    private void Start()
    {
        var canvas = rectTransform.root.GetComponent<Canvas>();
        scaleFactor = canvas.scaleFactor;
        root = canvas.transform;
    }

    public Sprite Icon
    {
        get => icon.sprite;
        set => icon.sprite = value;
    }

    public void SetParent(Transform parent)
    {
        rectTransform.SetParent(parent);
        rectTransform.anchoredPosition = Vector3.zero;
    }

    void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
    {
        if (!IsDraggable) return;

        prevParent = rectTransform.parent;
        rectTransform.SetParent(root);
        rectTransform.SetAsLastSibling();

        icon.raycastTarget = false;

        StartDrag?.Invoke();
    }

    void IDragHandler.OnDrag(PointerEventData eventData)
    {
        if (!IsDraggable) return;

        rectTransform.anchoredPosition += eventData.delta / scaleFactor;
    }

    void IEndDragHandler.OnEndDrag(PointerEventData eventData)
    {
        if (!IsDraggable) return;

        GameObject enterObject = eventData.pointerEnter;

        if (enterObject == null || !enterObject.TryGetComponent(out InventorySlotView slot))
        {
            SetParent(prevParent);
        }

        icon.raycastTarget = true;
        //IsDraggable = false;

        EndDrag?.Invoke(eventData);
    }
}

