using UnityEngine;

/// <summary>
/// Helper de testing temporal para previsualizar el <see cref="SaveSlotCardView"/> en el editor
/// sin necesidad de pasar por el flow completo del controller.
///
/// USO:
///   1. Drag & drop sobre el GameObject que tiene el SaveSlotCardView.
///   2. Asignar el campo "Test Slot" con cualquier SO_SaveSlotData.
///   3. Click derecho en el header → "Bind Now".
///
/// Para probar en runtime también, activar "Bind On Start".
///
/// Borrar este componente cuando termine el checkpoint visual.
/// </summary>
[RequireComponent(typeof(SaveSlotCardView))]
public class SaveSlotCardTester : MonoBehaviour
{
    [SerializeField] private SO_SaveSlotData testSlot;
    [SerializeField] private bool bindOnStart = true;

    private void Start()
    {
        if (bindOnStart) BindNow();
    }

    [ContextMenu("Bind Now")]
    private void BindNow()
    {
        if (testSlot == null)
        {
            Debug.LogWarning("[SaveSlotCardTester] Falta asignar el SO_SaveSlotData.");
            return;
        }
        SaveSlotCardView card = GetComponent<SaveSlotCardView>();
        card.Bind(testSlot);
    }
}
