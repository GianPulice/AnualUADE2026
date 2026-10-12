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

    [Tooltip("ON: every run (see RunSeed) shuffles which basket each box belongs in, among the " +
             "baskets of the table above. The baskets stay where they are and " +
             "ContainerPuzzleRunLayout repaints their symbols to match. OFF: the table is the answer.")]
    [SerializeField] private bool shuffleEachRun;

    // This run's table, and the seed it was shuffled from. Never serialized.
    [System.NonSerialized] private ContainerRequirement[] runRequirements;
    [System.NonSerialized] private int runRequirementsSeed;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;

    /// <summary>The answer for this run: the table as written, or its shuffle.</summary>
    public ContainerRequirement[] Requirements => shuffleEachRun ? RunRequirements() : requirements;

    /// <summary>
    /// The table as written. Each basket is built showing the symbol of the box this table puts in
    /// it, which is what <see cref="ContainerPuzzleRunLayout"/> starts from.
    /// </summary>
    public ContainerRequirement[] AuthoredRequirements => requirements;

    private ContainerRequirement[] RunRequirements()
    {
        if (requirements == null) return null;

        int seed = RunSeed.Value;
        if (runRequirements != null && runRequirementsSeed == seed) return runRequirements;

        // Fisher-Yates over the baskets: any order is as likely as any other, the written one included.
        string[] slots = new string[requirements.Length];
        for (int i = 0; i < slots.Length; i++) slots[i] = requirements[i].requiredSlotId;

        System.Random random = RunSeed.RandomFor(puzzleId);
        for (int i = slots.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        runRequirements = new ContainerRequirement[requirements.Length];
        for (int i = 0; i < requirements.Length; i++)
            runRequirements[i] = new ContainerRequirement { containerId = requirements[i].containerId, requiredSlotId = slots[i] };
        runRequirementsSeed = seed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var lines = new string[runRequirements.Length];
        for (int i = 0; i < lines.Length; i++) lines[i] = $"{runRequirements[i].containerId} → {runRequirements[i].requiredSlotId}";
        Debug.Log($"[{nameof(SO_ContainerPuzzleData)}] {puzzleId}: this run's baskets are {string.Join(", ", lines)}.");
#endif
        return runRequirements;
    }
}
