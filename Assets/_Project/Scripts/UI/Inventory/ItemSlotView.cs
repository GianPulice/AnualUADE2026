using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// VIEW of a single item row in the inventory list.
///
/// Responsibilities:
///   - Show the icon, name and color dot for the category
///   - Show the red left border when selected
///   - Notify the Controller of the click through a callback
///
/// <b>The selection visual is the animated Filled Horizontal sweep, on purpose.</b> A later pass
/// replaced it with a static 2px bar + tinted background, but that version painted two Images
/// (SelectionBar / RowBackground) that the InventoryItem prefab never had, so with the prefab as
/// authored nothing was highlighted at all and the list stopped reading as selectable. If that
/// design is picked up again, add and wire those two nodes in the prefab FIRST.
/// </summary>
public class ItemSlotView : MonoBehaviour,IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;
    // ── Serialized ────────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private Button selectButton;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image iconBackground;

    [Header("Row layout")]
    [Tooltip("Total character width of the row. Only lines up with a monospaced font " +
             "(ShareTechMono). Tune it to the real width of the list panel.")]
    [SerializeField] private int rowCharWidth = 28;

    [Header("Selected Fill")]
    [SerializeField] private Image selectionFillImage; // Filled Horizontal
    [SerializeField] private float fillDuration = 0.45f;
    [SerializeField] private float alphaDuration = 0.15f;
    [SerializeField] private float emptyMultiplier = 2.2f;

    [Header("Colors")]
    [SerializeField] private Color idleFillColor = Color.clear;
    [SerializeField] private Color selectedFillColor = Color.white;
    // ── State ─────────────────────────────────────────────────────────────────

    private Action<SO_InventoryItem> onClicked;

    private bool isHovering;
    private bool isSelected;

    private float currentFill;
    private float targetFill;

    private float currentAlpha;
    private float targetAlpha;

    public SO_InventoryItem Item { get; private set; }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(OnButtonClicked);

        if (selectionFillImage != null)
        {
            currentFill = 0f;
            currentAlpha = 0f;
            selectionFillImage.fillAmount = 0f;
            selectionFillImage.color = idleFillColor;
        }
    }

    void OnDestroy()
    {
        selectButton?.onClick.RemoveListener(OnButtonClicked);
    }
    private void Update()
    {
        TickVisuals();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the slot with an item, its row number and the click callback.
    /// Called by InventoryView when refreshing the list.
    /// </summary>
    public void Setup(SO_InventoryItem item, int index, Action<SO_InventoryItem> clickCallback)
    {
        Item = item;
        onClicked = clickCallback;

        var visuals = categoryConfig.Get(item.Category);

        itemNameText.text = BuildRowLabel(item, index, visuals.TagLabel);
        iconImage.sprite = item.ItemIcon;
        iconBackground.color = visuals.BackgroundColor;

        ApplyButtonColor(visuals.MainColor);
        SetFillColor(visuals.ButtonColor);

        ResetVisualState();
    }
    private void ResetVisualState()
    {
        isHovering = false;
        isSelected = false;

        currentFill = 0f;
        targetFill = 0f;

        currentAlpha = 0f;
        targetAlpha = 0f;

        if (selectionFillImage != null)
        {
            selectionFillImage.fillAmount = 0f;

            Color c = selectionFillImage.color;
            c.a = 0f;
            selectionFillImage.color = c;
        }
    }
    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (isSelected)
        {
            targetFill = 1f;
            targetAlpha = 1f;

            if (selectionFillImage != null)
                selectionFillImage.color = selectedFillColor;
        }
        else
        {
            targetFill = isHovering ? 1f : 0f;
            targetAlpha = isHovering ? 1f : 0f;

            if (selectionFillImage != null)
                selectionFillImage.color = idleFillColor;
        }
    }

    public void SetFillColor(Color color)
    {
        idleFillColor = color;
        selectedFillColor = color;

        if (!isSelected && selectionFillImage != null)
            selectionFillImage.color = color;
    }

    public void CancelHover()
    {
        isHovering = false;

        if (!isSelected)
            targetFill = 0f;
    }

    // -- POINTER EVENTS ---------------------------
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (selectButton == null || !selectButton.interactable)
            return;

        isHovering = true;

        if (!isSelected)
        {
            targetFill = 1f;

            if (selectionFillImage != null)
                selectionFillImage.color = idleFillColor;
        }
        targetAlpha = 1f;
        targetFill = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;

        if (!isSelected)
            targetFill = 0f;
        targetAlpha = isSelected ? 1f : 0f;
        targetFill = isSelected ? 1f : 0f;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void TickVisuals()
    {
        if (selectionFillImage == null)
            return;

        float speed = 1f / fillDuration;

        if (targetFill < currentFill)
            speed *= emptyMultiplier;

        currentFill = Mathf.MoveTowards(
            currentFill,
            targetFill,
            speed * Time.unscaledDeltaTime
        );

        selectionFillImage.fillAmount = currentFill;
        float alphaSpeed = 1f / alphaDuration;

        currentAlpha = Mathf.MoveTowards(
            currentAlpha,
            targetAlpha,
            alphaSpeed * Time.unscaledDeltaTime
        );

        Color c = selectionFillImage.color;
        c.a = currentAlpha;
        selectionFillImage.color = c;
    }

    public void OnButtonClicked()
    {
        onClicked?.Invoke(Item);
    }

    /// <summary>
    /// Builds the directory-listing row: "03 MECHANICAL_CORE ......[CMP]".
    /// Depends on the monospaced font — see InventoryTextFormat.
    /// </summary>
    private string BuildRowLabel(SO_InventoryItem item, int index, string tag)
    {
        string left  = index.ToString("00") + " " + InventoryTextFormat.MachineName(item.ItemName);
        string right = "[" + tag + "]";

        return InventoryTextFormat.DotLeader(left, right, rowCharWidth);
    }

    private void ApplyButtonColor(Color color)
    {
        if (selectButton == null) return;

        ColorBlock cb = selectButton.colors;

        cb.normalColor = color;
        cb.highlightedColor = color;
        cb.pressedColor = color;
        cb.selectedColor = color;
        cb.disabledColor = Color.gray;

        selectButton.colors = cb;
    }

}

