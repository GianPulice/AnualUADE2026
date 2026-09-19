using UnityEngine;

/// <summary>
/// Rules for items filed under <see cref="ItemCategory.Special"/> — the cores the Puzzle 1 hub
/// needs. Read by <see cref="InventoryManager.CanCarry"/>.
///
/// With the limit on, the player carries one Special at a time:
/// - a world pickup of a second one is refused, with <see cref="BlockedPickupFormat"/> as the reason;
/// - a puzzle that rewards a second one drops it on the floor instead (at the puzzle's reward drop
///   point, or in front of the player) as the item's <c>worldPickupPrefab</c>, to be picked up once
///   the one in hand has been inserted.
///
/// Off by default, so the game behaves exactly as before until someone turns it on.
/// </summary>
[CreateAssetMenu(fileName = "SO_SpecialItemRules", menuName = "Scriptable Objects/SO_SpecialItemRules")]
public class SO_SpecialItemRules : ScriptableObject
{
    [Tooltip("True = only one Special item (the hub cores) can be carried at a time.")]
    [SerializeField] private bool limitOneSpecialAtATime = false;

    [Header("Messages")]
    [Tooltip("Info line on a Special pickup the player cannot take yet. {0} is the item already carried.")]
    [SerializeField] private string blockedPickupFormat = "Hands full: insert the {0} first";

    [Tooltip("Global message when a puzzle reward is left on the floor. {0} is the dropped item.")]
    [SerializeField] private string rewardDroppedFormat = "{0} left behind: hands full";

    [Tooltip("Seconds the reward-dropped message stays on screen.")]
    [SerializeField, Min(0f)] private float rewardDroppedSeconds = 4f;

    public bool LimitOneSpecialAtATime => limitOneSpecialAtATime;
    public string BlockedPickupFormat => blockedPickupFormat;
    public string RewardDroppedFormat => rewardDroppedFormat;
    public float RewardDroppedSeconds => rewardDroppedSeconds;
}
