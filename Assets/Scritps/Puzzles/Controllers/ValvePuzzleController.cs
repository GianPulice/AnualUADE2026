using UnityEngine;

public class ValvePuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ValvePuzzleData valvePuzzleData;

    public string PuzzleId => valvePuzzleData != null ? valvePuzzleData.PuzzleId : string.Empty;

    public void CheckValves()
    {
        if (valvePuzzleData == null) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(valvePuzzleData.PuzzleId))
            return;

        foreach (SO_ValvePuzzleData.ValveRequirement requirement in valvePuzzleData.Requirements)
        {
            int currentPosition = PuzzleStateManager.Instance.GetValvePosition(requirement.valveId);

            if (currentPosition != requirement.requiredPosition)
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(valvePuzzleData.PuzzleId);

        if (valvePuzzleData.RewardItem != null)
            InventoryManager.Instance.AddItem(valvePuzzleData.RewardItem);

        Debug.Log($"Puzzle de válvulas completado: {valvePuzzleData.PuzzleId}");
    }
}
