using UnityEngine;

public class PuzzleController : MonoBehaviour
{
    [SerializeField] private SO_PuzzleData puzzleData;

    private PuzzleState currentState;

    public string PuzzleId => puzzleData != null ? puzzleData.PuzzleId : string.Empty;
    public PuzzleState CurrentState => currentState;
    public bool IsCompleted => currentState == PuzzleState.Completed;

    private void Awake()
    {
        if (puzzleData == null)
        {
            Debug.LogError($"PuzzleController without SO_PuzzleData on {gameObject.name}");
            return;
        }

        currentState = puzzleData.InitialState;

        // Exists rather than 'Instance != null': the property logs a warning of its own every time
        // it is read while null, so testing it as a null check spams the console on the very setup
        // it is meant to tolerate.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId))
        {
            currentState = PuzzleState.Completed;
        }
    }

    public void StartPuzzle()
    {
        if (currentState == PuzzleState.Completed) return;
        currentState = PuzzleState.InProgress;
    }

    public void CompletePuzzle()
    {
        if (puzzleData == null) return;
        if (currentState == PuzzleState.Completed) return;

        currentState = PuzzleState.Completed;

        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);
        else
            Debug.LogWarning($"[{nameof(PuzzleController)}] No PuzzleStateManager — completing " +
                             $"'{puzzleData.PuzzleId}' was not recorded, so nothing gated behind " +
                             $"it will open.", this);

        if (puzzleData.RewardItem != null)
        {
            if (InventoryManager.Exists) InventoryManager.Instance.AddItem(puzzleData.RewardItem);
            else Debug.LogWarning($"[{nameof(PuzzleController)}] No InventoryManager — the reward " +
                                  $"'{puzzleData.RewardItem.name}' for '{puzzleData.PuzzleId}' was " +
                                  $"not granted.", this);
        }

        Debug.Log($"Puzzle completed: {puzzleData.PuzzleId}");
    }
}
