using UnityEngine;

public class SequenceButtonInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SequencePanelInteractable panel;
    [SerializeField] private int buttonId;
    [SerializeField] private string promptText = "Presionar boton";


    public string GetInteractText()
    {
        return $"{promptText} {buttonId}";
    }

    public bool CanInteract()
    {
        return panel != null && panel.CanInteract();
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        panel.PressButton(buttonId);
    }

    public bool IsRepeatable()
    {
        return true;
    }
}
