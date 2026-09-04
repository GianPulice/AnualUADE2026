using UnityEngine;

[CreateAssetMenu(fileName = "SO_ContainerPuzzleData", menuName = "Scriptable Objects/Puzzles/Container Puzzle Data")]
public class SO_ContainerPuzzleData : ScriptableObject
{
    /// <summary>
    /// One line of the solution: "this box belongs in that basket".
    ///
    /// The field names say "container" because they predate the current implementation. There used
    /// to be a second, parallel box puzzle (ContainerInteractable + ContainerSlot) that wrote the
    /// same PuzzleStateManager keys with a different meaning, and the two overwrote each other. It
    /// has been deleted; the physical-box version is the only one left.
    /// </summary>
    [System.Serializable]
    public class ContainerRequirement
    {
        [Tooltip("Must match BallPuzzleItem.BallId on the box — NOT a container id, despite the " +
                 "field name. A typo here fails silently: the requirement simply never matches and " +
                 "the puzzle can never complete.")]
        public string containerId;

        [Tooltip("Must match BasketTrigger.basketId on the target basket. Same silent-failure " +
                 "warning as above.")]
        public string requiredSlotId;
    }

    [SerializeField] private string puzzleId;
    [SerializeField] private SO_InventoryItem rewardItem;

    [Tooltip("Every line must be satisfied at once for the puzzle to complete. " +
             "ContainerPuzzleController re-checks the whole table on each box entering or leaving " +
             "a basket.")]
    [SerializeField] private ContainerRequirement[] requirements;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;
    public ContainerRequirement[] Requirements => requirements;
}
