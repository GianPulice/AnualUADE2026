using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VIEW principal del inventario.
/// 
/// Responsabilidades:
///   - Mostrar/ocultar el panel con animación
///   - Instanciar y reciclar ItemSlotViews según la lista actual
///   - NO sabe nada de lógica de negocio
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class InventoryView : MonoBehaviour
{
    // ── Serialized ───────────────────────────────────────────────────────────

    [Header("Lista de ítems")]
    [SerializeField] private Transform itemListContainer;   // El ScrollView Content
    [SerializeField] private ItemSlotView itemSlotPrefab;

    [Header("Animación")]
    [SerializeField] private float fadeSpeed = 8f;

    // ── Privados ─────────────────────────────────────────────────────────────

    private CanvasGroup canvasGroup;
    private bool targetVisible = false;

    private List<ItemSlotView> activeSlots = new List<ItemSlotView>();
    private Queue<ItemSlotView> slotPool = new Queue<ItemSlotView>();

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    void Update()
    {
        AnimateVisibility();
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Muestra u oculta el panel de inventario.</summary>
    public void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;

        targetVisible = visible;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    /// <summary>
    /// Recibe el InventoryManager (Model) y actualiza la lista de slots.
    /// El Controller llama esto cada vez que el inventario cambia.
    /// </summary>
    public void RefreshList(InventoryManager inventoryManager)
    {
        if (itemListContainer == null || itemSlotPrefab == null)
        {
            Debug.LogError("Faltan referencias en InventoryView.");
            return;
        }

        // Reciclar todos los slots activos al pool
        foreach (ItemSlotView slot in activeSlots)
        {
            slot.gameObject.SetActive(false);
            slotPool.Enqueue(slot);
        }
        activeSlots.Clear();

        // Reconstruir con la lista actual
        foreach (SO_InventoryItem item in inventoryManager.GetItems())
        {
            ItemSlotView slot = GetOrCreateSlot();
            slot.Setup(item, OnSlotSelected);
            slot.gameObject.SetActive(true);
            activeSlots.Add(slot);
        }
    }

    // ── Privados ─────────────────────────────────────────────────────────────

    private void AnimateVisibility()
    {
        if (canvasGroup == null) return;
        float target = targetVisible ? 1f : 0f;
        canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, target, Time.deltaTime * fadeSpeed);
    }

    private ItemSlotView GetOrCreateSlot()
    {
        if (slotPool.Count > 0)
        {
            return slotPool.Dequeue();
        }

        return Instantiate(itemSlotPrefab, itemListContainer);
    }

    private void OnSlotSelected(SO_InventoryItem item)
    {
        InventoryManagerUI.Instance.SelectItem(item);
    }
}
