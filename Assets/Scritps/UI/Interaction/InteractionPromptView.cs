using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

public class InteractionPromptView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color infoColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private IInteractable currentTarget;

    private void Awake() => gameObject.SetActive(false);

    private void OnEnable()
    {
        InteractionEvents.OnTargetChanged += HandleTargetChanged;
        InventoryEvents.OnItemAdded       += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved     += HandleInventoryChanged;
    }

    private void OnDisable()
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
            HidePrompt().Forget();
    }

    private void HandleInventoryChanged(SO_InventoryItem _) => RefreshDisplay(animate: false);

    private void RefreshDisplay(bool animate)
    {
        if (currentTarget == null) return;

        if (currentTarget.CanInteract())
        {
            promptText.color = normalColor;
            promptText.text  = $"[E] {currentTarget.GetInteractText()}";
            if (animate) ShowPrompt().Forget();
        }
        else
        {
            string info = currentTarget.GetInfoText();
            if (!string.IsNullOrEmpty(info))
            {
                promptText.color = infoColor;
                promptText.text  = info;
                if (animate) ShowPrompt().Forget();
            }
            else
            {
                HidePrompt().Forget();
            }
        }
    }

    private async UniTaskVoid ShowPrompt()
    {
        gameObject.SetActive(true);
        await Fade(1f, 0.15f);
    }

    private async UniTaskVoid HidePrompt()
    {
        await Fade(0f, 0.15f);
        gameObject.SetActive(false);
    }
}
