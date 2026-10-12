using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SO_SequencePuzzleData", menuName = "Scriptable Objects/Puzzles/Sequence Puzzle Data")]
public class SO_SequencePuzzleData : ScriptableObject
{
    [SerializeField] private string puzzleId;
    [SerializeField] private string requiredSocketId;
    [SerializeField] private string promptText = "Interact with panel";
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private List<int> correctSequence = new List<int>();

    [Tooltip("Played (2D) when the right sequence is entered. Empty = silent — for a code that only " +
             "opens the way to the real step, whose completion is the one that sounds.")]
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_1_completo";

    public string PuzzleId => puzzleId;
    public string CompletedSoundId => completedSoundId;
    public string RequiredSocketId => requiredSocketId;
    public string PromptText => promptText;
    public SO_InventoryItem RewardItem => rewardItem;
    public IReadOnlyList<int> CorrectSequence => correctSequence;
}
