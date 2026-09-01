using UnityEngine;

public class PickupInteractable : BaseRangeInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;

    [Header("Audio")]
    [Tooltip("Id of the SO_SoundData to play when this item is picked up (must be registered in " +
             "AudioManager.sounds). Leave empty to skip audio on this pickup.")]
    [SerializeField] private string pickupSoundId = string.Empty;

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

        if (!string.IsNullOrEmpty(pickupSoundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(pickupSoundId, transform.position);

        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
