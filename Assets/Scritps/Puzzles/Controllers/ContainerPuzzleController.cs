using UnityEngine;

public class ContainerPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ContainerPuzzleData containerPuzzleData;

    public string PuzzleId => containerPuzzleData != null ? containerPuzzleData.PuzzleId : string.Empty;

    public void CheckContainers()
    {
        if (containerPuzzleData == null) return;

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
            InventoryManager.Instance.AddItem(containerPuzzleData.RewardItem);

        Debug.Log($"Puzzle de contenedores completado: {containerPuzzleData.PuzzleId}");
    }
}
