using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotView : MonoBehaviour, IDisposable, IPointerEnterHandler, IPointerExitHandler, IDropHandler//, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text amount;
    [SerializeField] private Image icon;
    [SerializeField] private ItemView item;
    [SerializeField] private RectTransform itemParent;

    private bool dropped;
    private IDisposable subscription;

    public event Action<InventorySlotView> Released;

    public event Action PointerEnter;
    public event Action PointerExit;
    public event Action ItemRemoved;

    [field: SerializeField] public AddWidget AddWidget { get; private set; }

    public string Title
    {
        get => title.text;
        set => title.text = value;
    }

    public string Amount
    {
        get => amount.text;
        set => amount.text = value;
    }

    public Sprite Icon
    {
        get => icon.sprite;
        set
        {
            icon.sprite = value;
            item.Icon = value;
        }
    }

    private void Start()
    {
        AddWidget.Hide();
    }

    private void OnEnable()
    {
        item.EndDrag += OnItemEndDrag;
    }

    private void OnDisable()
    {
        item.EndDrag -= OnItemEndDrag;
    }

    public void Dispose()
    {
        icon.sprite = null;
        item.Icon = null;
        title.text = string.Empty;
        amount.text = string.Empty;

        Released?.Invoke(this);
    }

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
    {
        AddWidget.Show();
        PointerEnter?.Invoke();
    }

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
    {
        AddWidget.Hide();
        PointerExit?.Invoke();
    }

    void IDropHandler.OnDrop(PointerEventData eventData)
    {
        dropped = false;

        if (eventData.pointerDrag.TryGetComponent(out ItemView dropedItem))
        {
            if (dropedItem.Equals(item))
            {
                dropped = true;
                dropedItem.SetParent(itemParent);
            }
        }
    }

    private void OnItemEndDrag(PointerEventData eventData)
    {
        if (!dropped)
            ItemRemoved?.Invoke();

        dropped = false;
    }
}