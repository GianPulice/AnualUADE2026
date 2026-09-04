using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VIEW of the discard confirmation dialog (spec §6).
///
/// Responsibilities (SRP):
///   - Show/hide the confirmation overlay
///   - Show the name of the item to discard
///   - Notify the Controller: confirm or cancel
///   - It does NOT remove the item — that is the Controller -> Model's responsibility
///
/// ESC closes this dialog without discarding.
/// The Controller manages the layer stack (ESC first closes this, then the inventory).
/// </summary>
public class DiscardDialogView : MonoBehaviour
{
    // ── Serialized ────────────────────────────────────────────────────────────

    [Header("Overlay")]
    [SerializeField] private GameObject overlayPanel;  // position absolute, inset 0

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI titleText;    // "Discard "{name}"?"
    [SerializeField] private TextMeshProUGUI warningText;  // "This action is permanent..."

    [Header("Buttons")]
    [SerializeField] private Button cancelButton;   // Neutral / grey
    [SerializeField] private Button confirmButton;  // Destructive red

    // ── State ─────────────────────────────────────────────────────────────────

    private SO_InventoryItem pendingItem;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        cancelButton?.onClick.AddListener(OnCancelClicked);
        confirmButton?.onClick.AddListener(OnConfirmClicked);
    }

    void OnDestroy()
    {
        cancelButton?.onClick.RemoveListener(OnCancelClicked);
        confirmButton?.onClick.RemoveListener(OnConfirmClicked);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the overlay with the item's name.
    /// Called by the Controller when it receives OnDiscardRequested.
    /// </summary>
    public void Show(SO_InventoryItem item)
    {
        pendingItem = item;

        if (titleText != null)
            titleText.text = $"> DISCARD {InventoryTextFormat.MachineName(item.ItemName)} ?";

        overlayPanel?.SetActive(true);
    }

    /// <summary>
    /// Hides the overlay. Called by the Controller on confirm or cancel.
    /// </summary>
    public void Hide()
    {
        pendingItem = null;
        overlayPanel?.SetActive(false);
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnConfirmClicked()
    {
        // The Controller runs InventoryManager.DiscardItem()
        InventoryManagerUI.Instance.ConfirmDiscard();
    }

    private void OnCancelClicked()
    {
        InventoryManagerUI.Instance.CancelDiscard();
    }
}
