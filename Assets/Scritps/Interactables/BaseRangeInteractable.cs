using UnityEngine;

// Base class for interactables. The name was kept for compatibility with the existing children.
// Detection is now done by the InteractionManager via a raycast from the centre of the camera;
// these components only describe "what I can do" and "how to interact".
// Requires a Collider on the same GameObject (or one on a child in the Interactable layer)
// so the raycast has something to hit.
public abstract class BaseRangeInteractable : MonoBehaviour, IInteractable
{
    protected virtual void Awake() { }

    public abstract string GetInteractText();

    public virtual string GetInfoText() => string.Empty;

    public bool CanInteract() => CanInteractInCloseRange();

    protected abstract bool CanInteractInCloseRange();

    public void Interact()
    {
        if (!CanInteract()) return;
        OnInteract();
    }

    protected abstract void OnInteract();

    public abstract bool IsRepeatable();
}
