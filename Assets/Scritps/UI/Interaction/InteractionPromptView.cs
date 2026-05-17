using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

public class InteractionPromptView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI promptText;
    private void Awake()
    {
        gameObject.SetActive(false);
    }
    private void OnEnable()
    {
        InteractionEvents.OnTargetChanged += HandleTargetChanged;
    }

    private void OnDisable()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
    }

    private void HandleTargetChanged(IInteractable target)
    {
        Debug.Log($"[UI] Evento recibido. Target es null? {target == null}");
        if (target != null)
        {
            promptText.text = $"[E] {target.GetInteractText()}";
            ShowPrompt().Forget();
        }
        else
        {
            HidePrompt().Forget();
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
