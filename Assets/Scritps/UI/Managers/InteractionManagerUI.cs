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


    private void ShowCurrentInteractionMessageText()
    {
        if (InteractionManager.Instance.CurrentInteractable != null)
        {
            if (InventoryManager.Instance.ReturnTrueIfInventoryIsFull())
            {
                interactionMessageText.text = "Inventario lleno";
                return;
            }

            else
            {
                interactionMessageText.text = "Presione la tecla 'E' para " + InteractionManager.Instance.CurrentInteractable.GetInteractText();
            }
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
