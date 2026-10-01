using UnityEngine;

public class ValvePuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ValvePuzzleData valvePuzzleData;
    [Tooltip("Where the reward is left if the player cannot carry it (one-Special-at-a-time rule). Empty = in front of the player.")]
    [SerializeField] private Transform rewardDropPoint;

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
        {
            if (TryGetCompletionSoundOrigin(out Vector3 origin))
                AudioManager.Instance.PlaySFX("sfx_subpuzzle_3_completo", origin);
            else
                AudioManager.Instance.PlaySFX("sfx_subpuzzle_3_completo");
        }

        PuzzleRewardDelivery.Deliver(valvePuzzleData.RewardItem, rewardDropPoint, this);

        Debug.Log($"Valve puzzle completed: {valvePuzzleData.PuzzleId}");
    }

    private bool TryGetCompletionSoundOrigin(out Vector3 origin)
    {
        origin = default;

        string valveId = valvePuzzleData.CompletionSoundValveId;
        if (string.IsNullOrWhiteSpace(valveId)) return false;

        foreach (ValveInteractable valve in FindObjectsByType<ValveInteractable>(FindObjectsInactive.Exclude))
        {
            if (valve.ValveId != valveId) continue;

            origin = valve.transform.position;
            return true;
        }

        Debug.LogWarning($"[{nameof(ValvePuzzleController)}] No valve '{valveId}' in the scene — the " +
                         "completion sound plays in 2D.", this);
        return false;
    }
}
