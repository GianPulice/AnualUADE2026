using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

public class InteractionPromptView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color infoColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField] private UISlideTransition slide;

    private IInteractable currentTarget;

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        InteractionEvents.OnTargetChanged        += HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested += HandlePromptRefreshRequested;
        InventoryEvents.OnItemAdded              += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            += HandleInventoryChanged;
        UIStateManager.OnModalPushed             += HandleModalPushed;
        UIStateManager.OnModalPopped             += HandleModalPopped;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged        -= HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested -= HandlePromptRefreshRequested;
        InventoryEvents.OnItemAdded              -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            -= HandleInventoryChanged;
        UIStateManager.OnModalPushed             -= HandleModalPushed;
        UIStateManager.OnModalPopped             -= HandleModalPopped;
    }

    private void HandlePromptRefreshRequested() => RefreshDisplay(animate: false);

    private void HandleTargetChanged(IInteractable target)
    {
        // Treat destroyed interactables (e.g. a pickup destroyed the frame it was consumed)
        // as null. IInteractable is an interface, so the raw `!= null` check on the field
        // skips UnityEngine.Object's fake-null overload — see IsAlive.
        if (!IsAlive(target)) target = null;

        currentTarget = target;

        if (target != null)
        {
            RefreshDisplay(animate: true);
            slide?.SlideIn(SlideDirection.FromBottom);
        }
        else
        {
            Fade(0f, 0.15f).Forget();
            slide?.SlideOut();
        }
    }

    private void HandleInventoryChanged(SO_InventoryItem _) => RefreshDisplay(animate: false);

    /// <summary>
    /// Any modal (inventory, pause, settings, sequence panel, document reader...) covers the
    /// prompt instantly. InteractionCanvas has sortingOrder 100 (the highest in the project),
    /// so without this the prompt would be drawn ON TOP of any modal.
    /// Snapping without animation on purpose: the modal may set Time.timeScale to 0.
    /// </summary>
    private void HandleModalPushed(IModalUI _)
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// Restore only if the InteractionManager still reports a live target — i.e. the player
    /// is right now aiming at something interactable. Restoring from a cached "last target"
    /// leaves a stale prompt pegged when the player looked away (or the target was destroyed)
    /// during the modal, and the raycast never fires a new TargetChanged because both the
    /// previous and current detected values are null. When there is no live target here, the
    /// next InteractionManager.Update fires a proper TargetChanged as soon as the raycast
    /// finds one — a one-frame gap invisible to the player.
    /// </summary>
    private void HandleModalPopped(IModalUI _)
    {
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;

        IInteractable live = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        if (!IsAlive(live)) return;

        currentTarget = live;
        RefreshDisplay(animate: true);
        slide?.SlideIn(SlideDirection.FromBottom);
    }

    private void RefreshDisplay(bool animate)
    {
        if (!IsAlive(currentTarget))
        {
            // The cached target was destroyed since the last update (typical after picking up
            // an item and then any UI event fires a refresh). Drop it so the prompt hides
            // cleanly instead of crashing on the next CanInteract call.
            currentTarget = null;
            Fade(0f, 0.15f).Forget();
            return;
        }

        if (currentTarget.CanInteract())
        {
            promptText.color = normalColor;
            promptText.text  = $"{currentTarget.GetInteractText()}";
            if (animate) Fade(1f, 0.15f).Forget();
        }
        else
        {
            string info = currentTarget.GetInfoText();
            if (!string.IsNullOrEmpty(info))
            {
                promptText.color = infoColor;
                promptText.text  = info;
                if (animate) Fade(1f, 0.15f).Forget();
            }
            else
            {
                Fade(0f, 0.15f).Forget();
            }
        }
    }

    /// <summary>
    /// True if the interactable is non-null AND — when it is a Unity object — not destroyed.
    /// A plain `== null` check on an <see cref="IInteractable"/> variable does NOT hit
    /// UnityEngine.Object's operator overload (it dispatches by static type), so a destroyed
    /// MonoBehaviour would slip through and throw MissingReferenceException on the next call.
    /// </summary>
    private static bool IsAlive(IInteractable target)
    {
        if (target == null) return false;
        if (target is UnityEngine.Object unityObj) return unityObj != null;
        return true;
    }
}
