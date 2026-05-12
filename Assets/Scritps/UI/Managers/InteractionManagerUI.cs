using TMPro;
using UnityEngine;

public class InteractionManagerUI : Singleton<InteractionManagerUI>
{
    [SerializeField] private TextMeshProUGUI interactionMessageText;


    void Awake()
    {
        CreateSingleton(false);
    }

    void Update()
    {
        ShowCurrentInteractionMessageText();
    }


    public void ShowCurrentInteractionMessageText()
    {
        if (interactionMessageText == null) return;

        if (InteractionManager.Instance == null || InteractionManager.Instance.CurrentInteractable == null)
        {
            ClearMessage();
            return;
        }

        string interactText = InteractionManager.Instance.CurrentInteractable.GetInteractText();

        if (string.IsNullOrWhiteSpace(interactText))
        {
            ClearMessage();
            return;
        }

        interactionMessageText.text = interactText;
    }

    private void ClearMessage()
    {
        if (interactionMessageText != null && interactionMessageText.text != string.Empty)
        {
            interactionMessageText.text = string.Empty;
        }
    }
}
