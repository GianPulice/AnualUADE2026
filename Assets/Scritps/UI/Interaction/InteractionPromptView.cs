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

    // Survives the forced hide from a modal (HandleModalPushed does not touch it): lets the
    // prompt reappear with the same target when the inventory/pause closes, without waiting
    // for the raycast to detect it again.
    private IInteractable lastKnownTarget;

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        InteractionEvents.OnTargetChanged += HandleTargetChanged;
        InventoryEvents.OnItemAdded       += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved     += HandleInventoryChanged;
        UIStateManager.OnModalPushed      += HandleModalPushed;
        UIStateManager.OnModalPopped      += HandleModalPopped;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
        InventoryEvents.OnItemAdded       -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved     -= HandleInventoryChanged;
        UIStateManager.OnModalPushed      -= HandleModalPushed;
        UIStateManager.OnModalPopped      -= HandleModalPopped;
    }

    private void HandleTargetChanged(IInteractable target)
    {
        currentTarget = target;
        if (target != null) lastKnownTarget = target;

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
    /// Snapping without animation on purpose: if the fade were running right when
    /// Time.timeScale goes to 0, Fade() (which uses deltaTime, not unscaled) could freeze halfway.
    /// </summary>
    private void HandleModalPushed(IModalUI _)
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// We only restore when the modal stack is completely empty (not with stacked modals,
    /// e.g. DiscardDialog over Inventory). It reappears with the last valid target without
    /// waiting for the player to look at it again.
    /// </summary>
    private void HandleModalPopped(IModalUI _)
    {
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;
        if (lastKnownTarget == null) return;

        currentTarget = lastKnownTarget;
        RefreshDisplay(animate: true);
        slide?.SlideIn(SlideDirection.FromBottom);
    }

    private void RefreshDisplay(bool animate)
    {
        if (currentTarget == null) return;

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
}
