using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VIEW del diálogo de confirmación de descarte (spec §6).
///
/// Responsabilidades (SRP):
///   - Mostrar/ocultar el overlay de confirmación
///   - Mostrar nombre del ítem a descartar
///   - Notificar al Controller: confirmar o cancelar
///   - NO elimina el ítem — eso es responsabilidad del Controller → Model
///
/// ESC cierra este diálogo sin descartar.
/// El Controller maneja la pila de capas (ESC primero cierra esto, luego el inventario).
/// </summary>
public class DiscardDialogView : MonoBehaviour
{
    // ── Serialized ────────────────────────────────────────────────────────────

    [Header("Overlay")]
    [SerializeField] private GameObject overlayPanel;  // position absolute, inset 0

    [Header("Textos")]
    [SerializeField] private TextMeshProUGUI titleText;    // "Descartar "{nombre}"?"
    [SerializeField] private TextMeshProUGUI warningText;  // "Esta acción es permanente..."

    [Header("Botones")]
    [SerializeField] private Button cancelButton;   // Neutro / gris
    [SerializeField] private Button confirmButton;  // Rojo destructivo

    // ── Estado ────────────────────────────────────────────────────────────────

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

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Muestra el overlay con el nombre del ítem.
    /// Llamado por el Controller al recibir OnDiscardRequested.
    /// </summary>
    public void Show(SO_InventoryItem item)
    {
        pendingItem = item;

        if (titleText != null)
            titleText.text = $"Descartar \"{item.ItemName}\"?";

        overlayPanel?.SetActive(true);
    }

    /// <summary>
    /// Oculta el overlay. Llamado por el Controller al confirmar o cancelar.
    /// </summary>
    public void Hide()
    {
        pendingItem = null;
        overlayPanel?.SetActive(false);
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnConfirmClicked()
    {
        // El Controller ejecuta InventoryManager.DiscardItem()
        InventoryManagerUI.Instance.ConfirmDiscard();
    }

    private void OnCancelClicked()
    {
        InventoryManagerUI.Instance.CancelDiscard();
    }
}
