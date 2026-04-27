using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SO_SequencePuzzleData", menuName = "Scriptable Objects/Puzzles/Sequence Puzzle Data")]
public class SO_SequencePuzzleData : ScriptableObject
{
    [SerializeField] private string puzzleId;
    [SerializeField] private string requiredSocketId;
    [SerializeField] private string promptText = "Interactuar con panel";
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private List<int> correctSequence = new List<int>();

    public string PuzzleId => puzzleId;
    public string RequiredSocketId => requiredSocketId;
    public string PromptText => promptText;
    public SO_InventoryItem RewardItem => rewardItem;
    public IReadOnlyList<int> CorrectSequence => correctSequence;
}
