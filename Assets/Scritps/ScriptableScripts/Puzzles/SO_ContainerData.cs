using UnityEngine;

[CreateAssetMenu(fileName = "SO_ContainerData", menuName = "Scriptable Objects/Puzzles/Container Data")]
public class SO_ContainerData : ScriptableObject
{
    [SerializeField] private string containerId;
    [SerializeField] private string linkedPuzzleId;
    [SerializeField] private string initialSlotId;
    [SerializeField] private string promptText = "Mover contenedor";

    public string ContainerId => containerId;
    public string LinkedPuzzleId => linkedPuzzleId;
    public string InitialSlotId => initialSlotId;
    public string PromptText => promptText;
}
