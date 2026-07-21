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

    // Sobrevive al hide forzado por modal (HandleModalPushed no lo toca): permite reaparecer
    // con el mismo target al cerrar el inventario/pausa, sin esperar a que el raycast lo
    // vuelva a detectar.
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
    /// Cualquier modal (inventario, pausa, settings, sequence panel, document reader...) tapa
    /// el prompt al instante. InteractionCanvas tiene sortingOrder 100 (el más alto del
    /// proyecto), así que sin esto el prompt quedaría dibujado ENCIMA de cualquier modal.
    /// Snap sin animación a propósito: si el fade corriera justo cuando Time.timeScale pasa a
    /// 0, Fade() (que usa deltaTime, no unscaled) podría congelarse a mitad de camino.
    /// </summary>
    private void HandleModalPushed(IModalUI _)
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// Solo restauramos cuando la pila de modales queda totalmente vacía (no con modales
    /// apiladas, ej. DiscardDialog sobre Inventario). Reaparece con el último target válido
    /// sin esperar a que el jugador vuelva a mirarlo.
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
