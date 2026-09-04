using UnityEngine;

public interface IInteractable
{
    string GetInteractText();
    string GetInfoText();
    bool CanInteract();
    void Interact();
    bool IsRepeatable();
}
