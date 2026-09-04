using UnityEngine;

public class ValvePuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ValvePuzzleData valvePuzzleData;

    public string PuzzleId => valvePuzzleData != null ? valvePuzzleData.PuzzleId : string.Empty;

    public void CheckValves()
    {
        if (valvePuzzleData == null) return;

        // One guard for the reads in the loop and the completion write below.
        if (!PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(valvePuzzleData.PuzzleId))
            return;

        foreach (SO_ValvePuzzleData.ValveRequirement requirement in valvePuzzleData.Requirements)
        {
            int currentPosition = PuzzleStateManager.Instance.GetValvePosition(requirement.valveId);

            if (currentPosition != requirement.requiredPosition)
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(valvePuzzleData.PuzzleId);

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_subpuzzle_completo");

        if (valvePuzzleData.RewardItem != null)
        {
            if (InventoryManager.Exists) InventoryManager.Instance.AddItem(valvePuzzleData.RewardItem);
            else Debug.LogWarning($"[{nameof(ValvePuzzleController)}] No InventoryManager — the " +
                                  $"reward '{valvePuzzleData.RewardItem.name}' for " +
                                  $"'{valvePuzzleData.PuzzleId}' was not granted.", this);
        }

        Debug.Log($"Valve puzzle completed: {valvePuzzleData.PuzzleId}");
    }
}
