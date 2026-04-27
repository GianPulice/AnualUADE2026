using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// VIEW de una fila de ítem en la lista del inventario.
/// 
/// Responsabilidades:
///   - Mostrar icono, nombre y dot de color según categoría
///   - Mostrar borde izquierdo rojo cuando está seleccionado
///   - Notificar el click al Controller a través de un callback
/// </summary>
public class ItemSlotView : MonoBehaviour,IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;
    // ── Serialized ───────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private Button selectButton;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image iconBackground;

    [Header("Selected Fill")]
    [SerializeField] private Image selectionFillImage; // Filled Horizontal
    [SerializeField] private float fillDuration = 0.45f;
    [SerializeField] private float alphaDuration = 0.15f;
    [SerializeField] private float emptyMultiplier = 2.2f;

    [Header("Colors")]
    [SerializeField] private Color idleFillColor = Color.clear;
    [SerializeField] private Color selectedFillColor = Color.white;
    // ── Estado ────────────────────────────────────────────────────────────────

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

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Configura el slot con un ítem y registra el callback de click.
    /// Llamado por InventoryView al refrescar la lista.
    /// </summary>
    public void Setup(SO_InventoryItem item, Action<SO_InventoryItem> clickCallback)
    {
        Item = item;
        onClicked = clickCallback;

        var visuals = categoryConfig.Get(item.Category);

        itemNameText.text = item.ItemName;
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

    // ── Privados ──────────────────────────────────────────────────────────────

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

