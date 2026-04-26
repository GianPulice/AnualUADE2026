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
        if (InteractionManager.Instance.CurrentInteractable != null)
        {
                interactionMessageText.text = "Presione la tecla 'E' para " + InteractionManager.Instance.CurrentInteractable.GetInteractText();
        }

        else
        {
            if (interactionMessageText.text != string.Empty)
            {
                interactionMessageText.text = string.Empty;
            }
        }
    }
}
