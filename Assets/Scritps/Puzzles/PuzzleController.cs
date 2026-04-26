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
            Debug.LogError($"PuzzleController sin SO_PuzzleData en {gameObject.name}");
            return;
        }

        currentState = puzzleData.InitialState;

        if (PuzzleStateManager.Instance != null &&
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
        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);

        if (puzzleData.RewardItem != null)
            InventoryManager.Instance.AddItem(puzzleData.RewardItem);

        Debug.Log($"Puzzle completado: {puzzleData.PuzzleId}");
    }
}
