using UnityEngine;

/// <summary>
/// Oculta este CanvasGroup al instante mientras haya cualquier modal abierta (inventario,
/// pausa, settings, sequence panel, document reader...) y lo restaura cuando se cierra la
/// última. Pensado para HUD que vive en un Canvas de sorting order muy alto (crosshair,
/// vignettes) que si no, queda dibujado ENCIMA de los menús.
///
/// Genérico y reutilizable: cualquier GameObject con CanvasGroup puede usarlo, no hardcodea
/// qué elemento de HUD es. Mismo patrón que ya usa InteractionPromptView.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ModalVisibilityGate : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        UIStateManager.OnModalPushed += HandleModalPushed;
        UIStateManager.OnModalPopped += HandleModalPopped;

        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen)
            SetVisible(false);
    }

    private void OnDestroy()
    {
        UIStateManager.OnModalPushed -= HandleModalPushed;
        UIStateManager.OnModalPopped -= HandleModalPopped;
    }

    private void HandleModalPushed(IModalUI _) => SetVisible(false);

    private void HandleModalPopped(IModalUI _)
    {
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
    }
}
