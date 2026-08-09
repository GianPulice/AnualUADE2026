using UnityEngine;

public class SequenceButtonInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SequencePanelInteractable panel;
    [SerializeField] private int buttonId;
    [SerializeField] private string promptText = "Press button";


    public string GetInfoText() => string.Empty;

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

        panel.TryPressButton(buttonId);
    }

    public bool IsRepeatable()
    {
        return true;
    }
}
