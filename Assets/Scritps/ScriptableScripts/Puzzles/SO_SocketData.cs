using UnityEngine;

[CreateAssetMenu(fileName = "SO_SocketData", menuName = "Scriptable Objects/Puzzles/Socket Data")]
public class SO_SocketData : ScriptableObject
{
    [SerializeField] private string socketId;
    [SerializeField] private string linkedPuzzleId;
    [SerializeField] private SO_InventoryItem requiredItem;
    [SerializeField] private bool consumeItem = true;
    [SerializeField] private string promptTextOverride;

    public string SocketId => socketId;
    public string LinkedPuzzleId => linkedPuzzleId;
    public SO_InventoryItem RequiredItem => requiredItem;
    public bool ConsumeItem => consumeItem;

    public string GetPromptText()
    {
        if (!string.IsNullOrWhiteSpace(promptTextOverride))
            return promptTextOverride;

        return requiredItem != null ? $"Insertar {requiredItem.ItemName}" : "Insertar item";
    }
}
