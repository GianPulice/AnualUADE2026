using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

public class InteractionPromptView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color infoColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private IInteractable currentTarget;

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        InteractionEvents.OnTargetChanged += HandleTargetChanged;
        InventoryEvents.OnItemAdded       += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved     += HandleInventoryChanged;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
        InventoryEvents.OnItemAdded       -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved     -= HandleInventoryChanged;
    }

    private void HandleTargetChanged(IInteractable target)
    {
        currentTarget = target;
        if (target != null)
            RefreshDisplay(animate: true);
        else
            Fade(0f, 0.15f).Forget();
    }

    private void HandleInventoryChanged(SO_InventoryItem _) => RefreshDisplay(animate: false);

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
