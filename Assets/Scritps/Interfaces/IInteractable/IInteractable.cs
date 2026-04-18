using UnityEngine;

public interface IInteractable
{
    string GetInteractText();
    bool CanInteract();
    void Interact();
}
