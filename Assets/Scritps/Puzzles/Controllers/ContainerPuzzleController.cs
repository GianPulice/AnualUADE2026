using UnityEngine;

public class ContainerPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ContainerPuzzleData containerPuzzleData;

    public string PuzzleId => containerPuzzleData != null ? containerPuzzleData.PuzzleId : string.Empty;

    public void CheckContainers()
    {
        if (containerPuzzleData == null) return;

        // Every read and the completion write below go through the manager; bailing once here
        // beats four separate guards. Singleton.Instance already logs on its own when it is null,
        // so there is nothing to add — this only has to stop the NullReferenceException.
        if (!PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(containerPuzzleData.PuzzleId))
            return;

        foreach (SO_ContainerPuzzleData.ContainerRequirement requirement in containerPuzzleData.Requirements)
        {
            string currentSlot = PuzzleStateManager.Instance.GetContainerSlot(requirement.containerId);

            if (currentSlot != requirement.requiredSlotId)
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(containerPuzzleData.PuzzleId);

        if (containerPuzzleData.RewardItem != null)
        {
            if (InventoryManager.Exists) InventoryManager.Instance.AddItem(containerPuzzleData.RewardItem);
            else Debug.LogWarning($"[{nameof(ContainerPuzzleController)}] No InventoryManager — the " +
                                  $"reward '{containerPuzzleData.RewardItem.name}' for " +
                                  $"'{containerPuzzleData.PuzzleId}' was not granted.", this);
        }

        Debug.Log($"Container puzzle completed: {containerPuzzleData.PuzzleId}");
    }
}
