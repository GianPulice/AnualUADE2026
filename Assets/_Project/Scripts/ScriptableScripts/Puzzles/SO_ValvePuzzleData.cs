using UnityEngine;

[CreateAssetMenu(fileName = "SO_ValvePuzzleData", menuName = "Scriptable Objects/Puzzles/Valve Puzzle Data")]
public class SO_ValvePuzzleData : ScriptableObject, IClueSource, IPuzzleRewardSource
{
    [System.Serializable]
    public class ValveRequirement
    {
        public string valveId;
        public int requiredPosition;

        [Tooltip("What the clue notes call this valve: the colour of its indicator, as the player " +
                 "sees it (\"yellow\").")]
        public string clueName;

        [Tooltip("Where this valve is, as a clue note says it after \"the valve\" to someone facing " +
                 "the valves (\"on the left\", \"in the center\").")]
        public string clueSide;
    }

    [SerializeField] private string puzzleId;
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private ValveRequirement[] requirements;

    [Tooltip("What plays when the puzzle is solved: the gas escaping, which is the valves' own sound and " +
             "not the generic sub-puzzle chime.")]
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_3_completo";

    [Tooltip("Id of the valve the completion sound plays from, in 3D. How far it carries is set on " +
             "its SO_SoundData (Max Distance + Rolloff). Empty = 2D.")]
    [SerializeField] private string completionSoundValveId;

    [Tooltip("ON: every run (see RunSeed) deals the Required Positions of the table out among its " +
             "valves at random, so the same rules (one untouched, one pointing down, one turned three " +
             "times) fall on different colours. The valves themselves never change. OFF: the table is " +
             "the answer.")]
    [SerializeField] private bool shuffleEachRun;

    // This run's table, and the seed it was shuffled from. Never serialized.
    [System.NonSerialized] private ValveRequirement[] runRequirements;
    [System.NonSerialized] private int runRequirementsSeed;

    public string PuzzleId => puzzleId;
    public SO_InventoryItem RewardItem => rewardItem;
    public string CompletedSoundId => completedSoundId;
    public string CompletionSoundValveId => completionSoundValveId;

    /// <summary>The answer for this run: the table as written, or its shuffle.</summary>
    public ValveRequirement[] Requirements => shuffleEachRun ? RunRequirements() : requirements;

    private ValveRequirement[] RunRequirements()
    {
        if (requirements == null) return null;

        int seed = RunSeed.Value;
        if (runRequirements != null && runRequirementsSeed == seed) return runRequirements;

        // Fisher-Yates over the positions: any deal is as likely as any other, the written one included.
        int[] positions = new int[requirements.Length];
        for (int i = 0; i < positions.Length; i++) positions[i] = requirements[i].requiredPosition;

        System.Random random = RunSeed.RandomFor(puzzleId);
        for (int i = positions.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        runRequirements = new ValveRequirement[requirements.Length];
        for (int i = 0; i < requirements.Length; i++)
            runRequirements[i] = new ValveRequirement
            {
                valveId = requirements[i].valveId,
                requiredPosition = positions[i],
                clueName = requirements[i].clueName,
                clueSide = requirements[i].clueSide,
            };
        runRequirementsSeed = seed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var lines = new string[runRequirements.Length];
        for (int i = 0; i < lines.Length; i++) lines[i] = $"{runRequirements[i].clueName} → {runRequirements[i].requiredPosition}";
        Debug.Log($"[{nameof(SO_ValvePuzzleData)}] {puzzleId}: this run's valves are {string.Join(", ", lines)}.");
#endif
        return runRequirements;
    }

    /// <summary>
    /// Names the valve that must end at position N this run: <c>{valve:N}</c> by its Clue Name
    /// ("The {valve:2} one has to point toward your feet"), <c>{side:N}</c> by its Clue Side
    /// ("NEVER touch the valve {side:0}").
    /// </summary>
    public string ResolveClue(string key, string format)
    {
        if ((key != "valve" && key != "side") || !int.TryParse(format, out int position)) return null;

        ValveRequirement[] table = Requirements;
        if (table == null) return null;

        foreach (ValveRequirement requirement in table)
            if (requirement.requiredPosition == position)
                return key == "valve" ? requirement.clueName : requirement.clueSide;
        return null;
    }
}
