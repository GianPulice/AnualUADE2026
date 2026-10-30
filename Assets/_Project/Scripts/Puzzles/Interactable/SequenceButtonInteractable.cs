using UnityEngine;

public class SequenceButtonInteractable : MonoBehaviour, IInteractable, IPuzzleInteractable
{
    [SerializeField] private SequencePanelInteractable panel;
    [SerializeField] private int buttonId;
    [SerializeField] private string promptText = "Press button";


    public string GetInfoText() => string.Empty;

    public string GetPromptText()
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

    /// <summary>Done when its panel is: a solved sequence takes no more presses.</summary>
    public bool IsFinished()
    {
        return panel != null && panel.IsFinished();
    }
}
