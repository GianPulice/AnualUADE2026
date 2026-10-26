using UnityEngine;

[CreateAssetMenu(fileName = "SO_HubPuzzleData", menuName = "Scriptable Objects/Puzzles/Hub Puzzle Data")]
public class SO_HubPuzzleData : ScriptableObject
{
    [SerializeField] private string puzzleId;
    [SerializeField] private string[] requiredSocketIds;

    [Tooltip("Played (2D) the moment the last socket completes the puzzle. Empty = silent.")]
    [SoundId, SerializeField] private string completedSoundId = string.Empty;

    public string PuzzleId => puzzleId;
    public string[] RequiredSocketIds => requiredSocketIds;
    public string CompletedSoundId => completedSoundId;
}
