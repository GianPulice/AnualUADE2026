using UnityEngine;

/// <summary>
/// Instantly hides this CanvasGroup while any modal is open (inventory, pause, settings,
/// sequence panel, document reader...) and restores it when the last one closes. Meant for
/// HUD that lives on a Canvas with a very high sorting order (crosshair, vignettes) which
/// would otherwise be drawn ON TOP of the menus.
///
/// Generic and reusable: any GameObject with a CanvasGroup can use it, it does not hardcode
/// which HUD element it is. Same pattern InteractionPromptView already uses.
///
/// <see cref="ignoredModalIds"/> lets an element stay up over specific modals: the module timer
/// keeps showing during the skill check, which is exactly when its penalties land. Empty = hide
/// under every modal, as before.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ModalVisibilityGate : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("IModalUI.ModalId values this element stays visible over (e.g. \"SkillCheck\"). " +
             "It still hides as soon as any other modal is open.")]
    [SerializeField] private string[] ignoredModalIds = new string[0];

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        UIStateManager.OnModalPushed += HandleModalChanged;
        UIStateManager.OnModalPopped += HandleModalChanged;

        // Only hide here, never force-show: at load the authored alpha stands.
        if (IsBlocked()) SetVisible(false);
    }

    private void OnDestroy()
    {
        UIStateManager.OnModalPushed -= HandleModalChanged;
        UIStateManager.OnModalPopped -= HandleModalChanged;
    }

    private void HandleModalChanged(IModalUI _) => SetVisible(!IsBlocked());

    private bool IsBlocked() =>
        UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpenExcept(ignoredModalIds);

    private void SetVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
    }
}
