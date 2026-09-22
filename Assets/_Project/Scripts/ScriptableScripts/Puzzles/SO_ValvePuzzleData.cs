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

    [Tooltip("Id of the valve the completion sound plays from, in 3D. How far it carries is set on " +
             "its SO_SoundData (Max Distance + Rolloff). Empty = 2D.")]
    [SerializeField] private string completionSoundValveId;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;
    public ValveRequirement[] Requirements => requirements;
    public string CompletionSoundValveId => completionSoundValveId;
}
