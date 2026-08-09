using UnityEngine;

public class PickupInteractable : BaseRangeInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;

    /// <summary>Item assigned to this pickup. Read by <see cref="ItemProximityHighlight"/>
    /// to resolve the category automatically without duplicating the dropdown by hand.</summary>
    public SO_InventoryItem Item => itemToPick;


    public override string GetInteractText()
    {
        return itemToPick != null
            ? $"Press 'E' to pick up {itemToPick.ItemName}"
            : "Press 'E' to pick up";
    }

    protected override bool CanInteractInCloseRange()
    {
        return itemToPick != null;
    }

    protected override void OnInteract()
    {
        AudioManager.Instance.PlaySFX("PickUpInteractable");
        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
