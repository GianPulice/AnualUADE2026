using UnityEngine;

public class PuzzleReward : MonoBehaviour
{
    [SerializeField] private SO_InventoryItem rewardItem;

    public void GiveReward()
    {
        if (rewardItem == null) return;

        InventoryManager.Instance.AddItem(rewardItem);
        Debug.Log($"Recompensa obtenida: {rewardItem.ItemName}");
    }
}
