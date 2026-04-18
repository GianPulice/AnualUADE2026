using UnityEngine;

public class PickupInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_InventoryItem itemToPick;


    public string GetInteractText()
    {
        return itemToPick != null ? $"Recoger {itemToPick.ItemName}" : "Recoger";
    }

    public bool CanInteract()
    {
        return itemToPick != null;
    }

    public void Interact()
    {
        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }
}
