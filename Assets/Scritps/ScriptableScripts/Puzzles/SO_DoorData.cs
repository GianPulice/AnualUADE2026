using UnityEngine;

[CreateAssetMenu(fileName = "SO_DoorData", menuName = "Scriptable Objects/Interactables/Door Data")]
public class SO_DoorData : ScriptableObject
{
    [SerializeField] private string doorId;
    [SerializeField] private SO_InventoryItem requiredKey;
    [SerializeField] private bool consumeKey = true;
    [SerializeField] private string requiredCompletedPuzzleId;
    [SerializeField] private string openPrompt = "Abrir puerta";
    [SerializeField] private string lockedPrompt = "Puerta cerrada";

    public string DoorId => doorId;
    public SO_InventoryItem RequiredKey => requiredKey;
    public bool ConsumeKey => consumeKey;
    public string RequiredCompletedPuzzleId => requiredCompletedPuzzleId;
    public string OpenPrompt => openPrompt;
    public string LockedPrompt => lockedPrompt;
}
