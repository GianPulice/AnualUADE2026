using UnityEngine;

public class PickupInteractable : BaseRangeInteractable, IPromptPresentation
{
    [Header("Item")]
    [Tooltip("The item this pickup hands to the inventory. A pickup with no item is INERT: " +
             "CanInteract() is false, the prompt view finds no info text either and hides " +
             "completely, so the player gets no message and E does nothing. Base prefabs " +
             "(NoteFather/Note, InventoryItemFather/InventoryItem) ship empty on purpose — " +
             "place one of their variants, or assign an item here.")]
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

    [Header("Reading")]
    [Tooltip("Put the note in front of the player the moment it is picked up, for items that " +
             "carry a document (Content Type = Text). Turn it off for a note that should only be " +
             "readable from the inventory.")]
    [SerializeField] private bool openReaderOnPickup = true;

    /// <summary>Item assigned to this pickup. Read by <see cref="ItemProximityHighlight"/>
    /// to resolve the category automatically without duplicating the dropdown by hand.</summary>
    public SO_InventoryItem Item => itemToPick;

    // -- IPromptPresentation -------------------
    // The prompt shows this as an item: its own title bar and the item's icon in the well, so
    // taking something off the floor does not look like opening a door.
    public InteractionPromptKind Kind => InteractionPromptKind.Item;
    public Sprite PromptIcon => itemToPick != null ? itemToPick.ItemIcon : null;


    /// <summary>
    /// Says so when this pickup can never be used.
    ///
    /// With no item, <see cref="CanInteractInCloseRange"/> returns false and
    /// <see cref="BaseRangeInteractable.GetInfoText"/> returns an empty string, so
    /// <c>InteractionPromptView</c> takes its else branch and fades the prompt out entirely.
    /// From the player's side the object is simply not interactable, with nothing anywhere
    /// saying why — which is why this costs an afternoon to find by hand.
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        if (itemToPick == null)
        {
            Debug.LogWarning(
                $"[{nameof(PickupInteractable)}] '{name}' has no Item To Pick assigned, so it " +
                "shows no prompt and does nothing when the player presses E. Assign an " +
                "SO_InventoryItem, or place one of the prefab variants instead of the base " +
                "prefab.", this);
        }
    }

    public override string GetInteractText()
    {
        return itemToPick != null
            ? $"Pick up {itemToPick.ItemName}"
            : "Pick up";
    }

    /// <summary>
    /// Why a Special cannot be taken yet (one-Special-at-a-time rule), naming the one in hand.
    /// Non-empty info text is also what keeps the prompt visible while E is refused.
    /// </summary>
    public override string GetInfoText()
    {
        if (itemToPick == null || !InventoryManager.Exists) return string.Empty;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory.CanCarry(itemToPick)) return string.Empty;

        SO_InventoryItem carried = inventory.CarriedSpecial;
        return carried != null && inventory.SpecialItemRules != null
            ? string.Format(inventory.SpecialItemRules.BlockedPickupFormat, carried.ItemName)
            : string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (itemToPick == null) return false;

        // No manager: let OnInteract run so its own warning explains why nothing happened.
        return !InventoryManager.Exists || InventoryManager.Instance.CanCarry(itemToPick);
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
        TryOpenReader();
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }

    /// <summary>
    /// Puts the note the player has just taken in front of them, on a frozen game.
    ///
    /// Content Type is the gate, not the category: it is the field that says "this item carries a
    /// document", and it is the same one <see cref="ItemDetailView"/> reads to decide whether the
    /// inventory gets an OPEN DOC button. A key filed under Note with no text stays silent.
    ///
    /// Called before Destroy(gameObject) — which is only queued until the end of the frame anyway.
    /// The reader lives on the LevelUI canvas and does not care that this object is going away.
    /// </summary>
    private void TryOpenReader()
    {
        if (!openReaderOnPickup) return;
        if (itemToPick.ContentType != ItemContentType.Text) return;
        if (string.IsNullOrWhiteSpace(itemToPick.TextContent)) return;

        if (DocumentReaderController.Instance == null)
        {
            Debug.LogWarning($"[{nameof(PickupInteractable)}] No DocumentReaderController in the " +
                             $"loaded scenes (it lives on LevelUI), so '{itemToPick.ItemName}' " +
                             "went to the inventory without being read.", this);
            return;
        }

        DocumentReaderController.Instance.Open(itemToPick);
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
