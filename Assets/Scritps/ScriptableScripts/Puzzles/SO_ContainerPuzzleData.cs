using UnityEngine;

[CreateAssetMenu(fileName = "SO_ContainerPuzzleData", menuName = "Scriptable Objects/Puzzles/Container Puzzle Data")]
public class SO_ContainerPuzzleData : ScriptableObject
{
    [System.Serializable]
    public class ContainerRequirement
    {
        public string containerId;
        public string requiredSlotId;
    }

    [SerializeField] private string puzzleId;
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private ContainerRequirement[] requirements;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;
    public ContainerRequirement[] Requirements => requirements;
}
