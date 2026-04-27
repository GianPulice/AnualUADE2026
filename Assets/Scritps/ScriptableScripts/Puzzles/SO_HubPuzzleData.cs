using UnityEngine;

[CreateAssetMenu(fileName = "SO_HubPuzzleData", menuName = "Scriptable Objects/Puzzles/Hub Puzzle Data")]
public class SO_HubPuzzleData : ScriptableObject
{
    [SerializeField] private string puzzleId;
    [SerializeField] private string[] requiredSocketIds;

    public string PuzzleId => puzzleId;
    public string[] RequiredSocketIds => requiredSocketIds;
}
