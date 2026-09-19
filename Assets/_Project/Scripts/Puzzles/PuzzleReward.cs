using UnityEngine;

public class PuzzleReward : MonoBehaviour
{
    [SerializeField] private SO_InventoryItem rewardItem;
    [Tooltip("Where the reward is left if the player cannot carry it (one-Special-at-a-time rule). Empty = in front of the player.")]
    [SerializeField] private Transform rewardDropPoint;

    public void GiveReward()
    {
        if (rewardItem == null) return;

        PuzzleRewardDelivery.Deliver(rewardItem, rewardDropPoint, this);
        Debug.Log($"Reward obtained: {rewardItem.ItemName}");
    }
}
