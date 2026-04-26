using UnityEngine;

[CreateAssetMenu(fileName = "SO_ValveData", menuName = "Scriptable Objects/Puzzles/Valve Data")]
public class SO_ValveData : ScriptableObject
{
    [SerializeField] private string valveId;
    [SerializeField] private string linkedPuzzleId;
    [SerializeField] private int maxPositions = 4;
    [SerializeField] private int initialPosition = 0;
    [SerializeField] private string promptText = "Girar válvula";

    public string ValveId => valveId;
    public string LinkedPuzzleId => linkedPuzzleId;
    public int MaxPositions => maxPositions;
    public int InitialPosition => initialPosition;
    public string PromptText => promptText;
}
