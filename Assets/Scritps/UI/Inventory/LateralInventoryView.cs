using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// View base del **Inventario Lateral** — la versión reducida del inventario que se usa
/// durante la Variante B de interacción con puzzles (spec interaction §6.2):
///
///   "Al presionar E sobre el puzzle, la cámara hace un paneo cinematico y se abre un
///    panel de inventario en el lado izquierdo. El jugador selecciona el item correcto
///    y lo coloca. El panel muestra solo los items del inventario, sin el panel de detalle."
///
/// ESTADO: Esqueleto inicial. Funcionalidad básica (mostrar items, emitir evento de selección).
/// PENDIENTE para cuando se implemente la Variante B:
///   - Hook al PlayerController.SetState(Interacting) para bloquear input gameplay.
///   - Paneo de cámara hacia <c>puzzleCameraPoint</c> (Lerp 0.6s).
///   - Navegación con mouse/gamepad sobre la lista.
///   - Feedback shake/sonido cuando el item es incorrecto (spec interaction §10).
///   - Cancelación por ESC.
///   - Interrupción por NemesisController.OnDangerDetected (spec interaction §10).
///
/// Esta clase está pensada para ser controlada por el Interactable del puzzle (Variante B),
/// no por un controller MVC. El puzzle decide cuándo abrirla y qué hacer con la selección.
/// </summary>
public class LateralInventoryView : BaseScreenView
{
    [Header("Refs")]
    [SerializeField] private Transform itemListContainer;
    [SerializeField] private LateralInventorySlotView slotPrefab;

    /// <summary>Disparado cuando el player hace click sobre un item de la lista.</summary>
    public event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>Disparado cuando el player cancela (ESC, click fuera, etc.).</summary>
    public event Action OnCancelled;

    private readonly List<LateralInventorySlotView> _spawnedSlots = new List<LateralInventorySlotView>();

    /// <summary>
    /// Puebla la lista con los items provistos. La Variante B típicamente pasa
    /// <c>InventoryManager.Instance.GetAllItems()</c>, opcionalmente filtrados por
    /// categoría (ej: solo "Component" para el Hub Central).
    /// </summary>
    public void Populate(IReadOnlyList<SO_InventoryItem> items)
    {
        ClearSlots();

        if (items == null || slotPrefab == null || itemListContainer == null) return;

        foreach (SO_InventoryItem item in items)
        {
            if (item == null) continue;
            LateralInventorySlotView slot = Instantiate(slotPrefab, itemListContainer);
            slot.Bind(item);
            slot.OnClicked += HandleSlotClicked;
            _spawnedSlots.Add(slot);
        }
    }

    /// <summary>
    /// Feedback de error cuando el item seleccionado no es válido para este puzzle.
    /// Hoy es un stub — implementación visual queda para cuando se haga la Variante B.
    /// </summary>
    public void ShowInvalidSelectionFeedback(SO_InventoryItem item)
    {
        // TODO: shake del slot + sonido de error (spec interaction §9.2).
    }

    /// <summary>Llamado externamente para cancelar (ej: por ESC del controller del puzzle).</summary>
    public void NotifyCancelled() => OnCancelled?.Invoke();

    private void OnDestroy()
    {
        ClearSlots();
    }

    private void ClearSlots()
    {
        foreach (LateralInventorySlotView slot in _spawnedSlots)
        {
            if (slot == null) continue;
            slot.OnClicked -= HandleSlotClicked;
            Destroy(slot.gameObject);
        }
        _spawnedSlots.Clear();
    }

    private void HandleSlotClicked(SO_InventoryItem item) => OnItemSelected?.Invoke(item);
}
