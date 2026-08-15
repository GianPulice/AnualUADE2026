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
        // Checked before anything else: this method destroys the pickup, so handing the item to a
        // manager that is not there would delete it from the level for good.
        if (!InventoryManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PickupInteractable)}] No InventoryManager — leaving " +
                             $"'{itemToPick.ItemName}' in the level rather than destroying it.", this);
            return;
        }

        if (AudioManager.Exists) AudioManager.Instance.PlaySFX("PickUpInteractable");

        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
