using UnityEngine;

public class PickupInteractable : BaseRangeInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;


    public override string GetInteractText()
    {
        return itemToPick != null
            ? $"Presione la tecla 'E' para agarrar {itemToPick.ItemName}"
            : "Presione la tecla 'E' para agarrar";
    }

    protected override bool CanInteractInCloseRange()
    {
        return itemToPick != null;
    }

    protected override void OnInteract()
    {
        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
