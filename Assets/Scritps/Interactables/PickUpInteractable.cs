using UnityEngine;

public class PickupInteractable : BaseRangeInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;

    [Header("Audio")]
    [Tooltip("SO_SoundData played when THIS pickup is taken. Leave empty to use the default for " +
             "the item's category from the Category Config below — which is the normal case, so " +
             "that two keys sound the same and a key does not sound like a note.")]
    [SoundId]
    [SerializeField] private string pickupSoundId = string.Empty;

    [Tooltip("Where the per-category default pickup sound comes from. Leave empty and only the " +
             "explicit Pickup Sound Id above is used.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

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

        PlayPickupSound();

        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }

    /// <summary>
    /// Plays this pickup's own sound, falling back to the default for the item's category.
    ///
    /// The fallback is the point: <see cref="pickupSoundId"/> ships empty on every prefab, so
    /// before this every pickup in the game was silent unless someone had filled the field in by
    /// hand — and a silent pickup is indistinguishable from one whose id has a typo.
    ///
    /// Positioned, so it is 3D and comes from the object rather than from inside the player's head.
    /// </summary>
    private void PlayPickupSound()
    {
        if (!AudioManager.Exists) return;

        string soundId = pickupSoundId;

        if (string.IsNullOrWhiteSpace(soundId) && categoryConfig != null && itemToPick != null)
            soundId = categoryConfig.Get(itemToPick.Category).pickupSoundId;

        if (string.IsNullOrWhiteSpace(soundId)) return;

        AudioManager.Instance.PlaySFX(soundId, transform.position);
    }
}
