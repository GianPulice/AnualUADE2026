using UnityEngine;

public class DoorInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_InventoryItem requiredItem;
    [SerializeField] private bool isLocked;
    [SerializeField] private bool consumeItem;
    [SerializeField] private bool isOpen;


    public string GetInteractText()
    {
        if (isLocked)
        {
            if (requiredItem != null)
                return $"Abrir puerta (requiere {requiredItem.ItemName})";

            return "Puerta cerrada";
        }

        return isOpen ? "Cerrar puerta" : "Abrir puerta";
    }

    public bool CanInteract()
    {
        if (!isLocked) return true;

        if (requiredItem == null) return false;

        return InventoryManager.Instance.HasItem(requiredItem);
    }

    public void Interact()
    {
        if (isLocked)
        {
            if (requiredItem != null && InventoryManager.Instance.HasItem(requiredItem))
            {
                isLocked = false;

                if (consumeItem)
                    InventoryManager.Instance.RemoveItem(requiredItem);
            }

            else
            {
                Debug.Log("No tenés el item requerido.");
                return;
            }
        }

        isOpen = !isOpen;

        Debug.Log(isOpen ? "Puerta abierta" : "Puerta cerrada");
    }
}
