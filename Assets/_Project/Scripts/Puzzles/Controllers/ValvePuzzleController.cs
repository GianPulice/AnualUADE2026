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

        // The valves' own sound, the gas escaping, and not the generic chime: from the valve the puzzle
        // names, so it comes out of the pipes; in 2D when none is named or it is not in the scene.
        if (AudioManager.Exists && !string.IsNullOrWhiteSpace(valvePuzzleData.CompletedSoundId))
        {
            if (TryGetCompletionSoundOrigin(out Vector3 origin))
                AudioManager.Instance.PlaySFX(valvePuzzleData.CompletedSoundId, origin);
            else
                AudioManager.Instance.PlaySFX(valvePuzzleData.CompletedSoundId);
        }

        PuzzleRewardDelivery.Deliver(valvePuzzleData.PuzzleId, valvePuzzleData.RewardItem, rewardDropPoint, this);

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
