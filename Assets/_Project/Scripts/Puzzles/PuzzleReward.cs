using UnityEngine;

public class PuzzleReward : MonoBehaviour
{
    [SerializeField] private SO_InventoryItem rewardItem;

    public void GiveReward()
    {
        if (rewardItem == null) return;

        if (!InventoryManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PuzzleReward)}] No InventoryManager — the reward " +
                             $"'{rewardItem.ItemName}' was not granted.", this);
            return;
        }

        InventoryManager.Instance.AddItem(rewardItem);
        Debug.Log($"Reward obtained: {rewardItem.ItemName}");
    }
}
