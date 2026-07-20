using UnityEngine;

// Clase base de interactuables. Se mantuvo el nombre por compatibilidad con los hijos existentes.
// La deteccion ahora la hace InteractionManager via raycast desde el centro de la camara;
// estos componentes solo describen "que puedo hacer" y "como interactuar".
// Requiere un Collider en el mismo GameObject (o uno en un hijo en la layer Interactable)
// para que el raycast tenga contra que pegar.
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
