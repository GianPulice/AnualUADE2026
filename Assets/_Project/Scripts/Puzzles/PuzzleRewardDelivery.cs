using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single path for every puzzle that hands out an item on completion.
///
/// Normally the reward goes straight to the inventory (<see cref="InventoryManager.AddItemAuto"/>).
/// When the player cannot carry it — a second Special under <see cref="SO_SpecialItemRules"/> —
/// it is left on the floor as the item's <see cref="SO_InventoryItem.WorldPickupPrefab"/> instead,
/// so solving a puzzle while already holding a core never loses the new one: the player inserts the
/// one in hand and comes back for it.
/// </summary>
public static class PuzzleRewardDelivery
{
    // Fallback placement when the puzzle has no drop point: this far in front of the player, then
    // straight down onto whatever floor is there.
    private const float FallbackForwardDistance = 1f;
    private const float GroundProbeHeight = 1f;
    private const float GroundProbeDistance = 5f;
    // The pickups have a gravity Rigidbody and a 1m box centred on the pivot: spawned half a metre
    // up, they settle on the floor instead of starting inside it.
    private const float SpawnLift = 0.5f;

    /// <param name="dropPoint">Where the reward lands if it cannot be carried. Null = in front of the player.</param>
    /// <param name="context">The puzzle granting it. Used for logs and to keep the dropped pickup in its scene.</param>
    public static void Deliver(SO_InventoryItem item, Transform dropPoint, Component context)
    {
        if (item == null) return;

        if (!InventoryManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PuzzleRewardDelivery)}] No InventoryManager — the reward " +
                             $"'{item.name}' was not granted.", context);
            return;
        }

        InventoryManager inventory = InventoryManager.Instance;

        if (inventory.CanCarry(item) || !TryDrop(item, dropPoint, context))
        {
            inventory.AddItemAuto(item);
            return;
        }

        SO_SpecialItemRules rules = inventory.SpecialItemRules;
        if (rules != null)
            InteractionEvents.RaiseGlobalMessage(
                string.Format(rules.RewardDroppedFormat, item.ItemName), rules.RewardDroppedSeconds);
    }

    private static bool TryDrop(SO_InventoryItem item, Transform dropPoint, Component context)
    {
        if (item.WorldPickupPrefab == null)
        {
            // Better to break the rule than to lose a core the hub needs.
            Debug.LogWarning($"[{nameof(PuzzleRewardDelivery)}] '{item.name}' has no World Pickup " +
                             "Prefab, so it cannot be left on the floor — granting it anyway, over the " +
                             "one-Special-at-a-time limit.", context);
            return false;
        }

        Vector3 position;
        Quaternion rotation;

        if (dropPoint != null)
        {
            position = dropPoint.position;
            rotation = dropPoint.rotation;
        }
        else
        {
            position = FallbackPosition(context);
            rotation = Quaternion.identity;
        }

        PickupInteractable pickup = Object.Instantiate(item.WorldPickupPrefab, position, rotation);

        // Instantiate lands in the active scene, which with additive loading may not be the level.
        // Moved next to the puzzle so it unloads with it.
        if (context != null && context.gameObject.scene.IsValid())
            SceneManager.MoveGameObjectToScene(pickup.gameObject, context.gameObject.scene);

        Debug.Log($"[{nameof(PuzzleRewardDelivery)}] Hands full — '{item.ItemName}' left at {position}.");
        return true;
    }

    private static Vector3 FallbackPosition(Component context)
    {
        Transform player = PlayerRegistry.CurrentTransform;
        Transform origin = player != null ? player : context != null ? context.transform : null;
        if (origin == null) return Vector3.zero;

        Vector3 forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up).normalized;
        Vector3 probe = origin.position + forward * FallbackForwardDistance + Vector3.up * GroundProbeHeight;

        return Physics.Raycast(probe, Vector3.down, out RaycastHit hit, GroundProbeDistance,
                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point + Vector3.up * SpawnLift
            : origin.position;
    }
}
