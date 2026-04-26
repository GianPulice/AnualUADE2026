using UnityEngine;

[CreateAssetMenu(fileName = "SO_PuzzleData", menuName = "Scriptable Objects/Puzzles/Puzzle Data")]
public class SO_PuzzleData : ScriptableObject
{
    [SerializeField] private string puzzleId;
    [SerializeField] private PuzzleState initialState = PuzzleState.NotStarted;
    [SerializeField] private SO_InventoryItem rewardItem;

    public string PuzzleId => puzzleId;
    public PuzzleState InitialState => initialState;
    public SO_InventoryItem RewardItem => rewardItem;
}
