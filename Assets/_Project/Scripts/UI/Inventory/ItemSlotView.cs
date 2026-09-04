using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// VIEW of a single item row in the inventory list.
///
/// Responsibilities:
///   - Show the icon, name and category tag
///   - Show the red left bar and tinted background when selected
///   - Notify the Controller of the click through a callback
///
/// <b>The selection visual is static, not animated.</b> It used to be a Filled Horizontal image
/// sweeping across the row over 0.45s, which meant every row ran an Update() for the life of the
/// panel and the list read as a game menu rather than as an application. It is now the same 2px
/// accent bar + tinted row that SettingsTabSelector uses for its active tab, so the two screens
/// agree on what "selected" looks like — and the per-row Update is gone.
///
/// <b>The category is not communicated by colour any more.</b> That is what the [CMP] / [KEY] tag
/// in the row label is for. Colour here means state — selected, hovered — and nothing else, which
/// is what lets the whole list be repainted from one palette.
/// </summary>
public class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    // ── Serialized ────────────────────────────────────────────────────────────

    [SerializeField] private Button selectButton;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image iconBackground;

    [Header("Row layout")]
    [Tooltip("Total character width of the row. Only lines up with a monospaced font " +
             "(ShareTechMono). Tune it to the real width of the list panel.")]
    [SerializeField] private int rowCharWidth = 28;

    [Header("Selection visuals")]
    [Tooltip("Palette the row states are read from. Without it the row falls back to the colours " +
             "below, so a slot with no theme still works — it just stops following the design.")]
    [SerializeField] private SO_UIThemeConfig theme;

    [Tooltip("The 2px bar down the left edge of the row. Shown only while selected.")]
    [SerializeField] private Image selectionBar;

    [Tooltip("Full-width background behind the row. Tinted while selected or hovered, transparent " +
             "otherwise.")]
    [SerializeField] private Image rowBackground;

    [Header("Fallback colours (used when no theme is assigned)")]
    [SerializeField] private Color fallbackAccent = new Color(0.80f, 0.10f, 0.10f, 1f);
    [SerializeField] private Color fallbackSelectedBg = new Color(0.10f, 0.03f, 0.03f, 1f);
    [SerializeField] private Color fallbackHoverBg = new Color(0.14f, 0.14f, 0.15f, 1f);

    // ── State ─────────────────────────────────────────────────────────────────

    private Action<SO_InventoryItem> onClicked;

    private bool isHovering;
    private bool isSelected;

    public SO_InventoryItem Item { get; private set; }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (selectButton != null) selectButton.onClick.AddListener(OnButtonClicked);

        // Neutralised once, here, rather than tinted per category as it used to be. The Button's
        // own ColorBlock MULTIPLIES whatever its target Graphic is showing, so leaving it coloured
        // would fight every colour this class writes and the winner would depend on which ran last.
        // White means "multiply by 1" and leaves exactly one writer of visible colour: RefreshVisuals.
        NeutraliseButtonTint();

        RefreshVisuals();
    }

    private void OnDestroy()
    {
        selectButton?.onClick.RemoveListener(OnButtonClicked);
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

        // Reset here and not only in Awake: rows come from a pool, so a slot arrives carrying the
        // selection and hover state of whatever item used it last.
        isHovering = false;
        isSelected = false;
        RefreshVisuals();
    }

    public void SetSelected(bool selected)
    {
        if (isSelected == selected) return;

        isSelected = selected;
        RefreshVisuals();
    }

    // ── Pointer events ────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (selectButton == null || !selectButton.interactable) return;

        isHovering = true;
        RefreshVisuals();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        RefreshVisuals();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the whole row's state in one place, from the two booleans. Selection outranks hover:
    /// moving the mouse off the selected row must not make it look unselected.
    /// </summary>
    private void RefreshVisuals()
    {
        if (selectionBar != null)
        {
            selectionBar.color = Token(UIThemeRole.Accent, fallbackAccent);
            selectionBar.enabled = isSelected;
        }

        if (rowBackground == null) return;

        if (isSelected)
        {
            rowBackground.color = Token(UIThemeRole.AccentBgSubtle, fallbackSelectedBg);
            rowBackground.enabled = true;
        }
        else if (isHovering)
        {
            rowBackground.color = Token(UIThemeRole.SurfaceRaised, fallbackHoverBg);
            rowBackground.enabled = true;
        }
        else
        {
            // Disabled rather than made transparent: an alpha-0 Image still draws and still catches
            // raycasts, and there is one of these per row.
            rowBackground.enabled = false;
        }
    }

    private Color Token(UIThemeRole role, Color fallback) =>
        theme != null ? theme.Get(role) : fallback;

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

    /// <summary>
    /// Makes the Button's colour transition a no-op so it cannot tint over the row's own state
    /// colours. Disabled stays dimmed, because a non-interactable row still has to read as one.
    /// </summary>
    private void NeutraliseButtonTint()
    {
        if (selectButton == null) return;

        ColorBlock cb = selectButton.colors;

        cb.normalColor      = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor     = Color.white;
        cb.selectedColor    = Color.white;
        cb.disabledColor    = new Color(1f, 1f, 1f, 0.4f);

        selectButton.colors = cb;
    }
}
