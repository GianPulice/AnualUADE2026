using UnityEngine;

[CreateAssetMenu(fileName = "SO_ValvePuzzleData", menuName = "Scriptable Objects/Puzzles/Valve Puzzle Data")]
public class SO_ValvePuzzleData : ScriptableObject
{
    [System.Serializable]
    public class ValveRequirement
    {
        public string valveId;
        public int requiredPosition;
    }

    [SerializeField] private string puzzleId;
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private ValveRequirement[] requirements;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;
    public ValveRequirement[] Requirements => requirements;
}
