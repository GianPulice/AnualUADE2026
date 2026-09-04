using UnityEngine;

/// <summary>
/// Instantly hides this CanvasGroup while any modal is open (inventory, pause, settings,
/// sequence panel, document reader...) and restores it when the last one closes. Meant for
/// HUD that lives on a Canvas with a very high sorting order (crosshair, vignettes) which
/// would otherwise be drawn ON TOP of the menus.
///
/// Generic and reusable: any GameObject with a CanvasGroup can use it, it does not hardcode
/// which HUD element it is. Same pattern InteractionPromptView already uses.
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
