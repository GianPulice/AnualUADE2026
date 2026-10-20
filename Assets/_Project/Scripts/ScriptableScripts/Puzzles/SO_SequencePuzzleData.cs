using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SO_SequencePuzzleData", menuName = "Scriptable Objects/Puzzles/Sequence Puzzle Data")]
public class SO_SequencePuzzleData : ScriptableObject, IClueSource, IPuzzleRewardSource
{
    [SerializeField] private string puzzleId;
    [SerializeField] private string requiredSocketId;
    [SerializeField] private string promptText = "Interact with panel";
    [SerializeField] private SO_InventoryItem rewardItem;
    [SerializeField] private List<int> correctSequence = new List<int>();

    [Header("Run randomization")]
    [Tooltip("ON: every run draws its own code (see RunSeed), as long as Correct Sequence, each digit " +
             "between Min Digit and Max Digit. Correct Sequence then only gives the length. " +
             "OFF: Correct Sequence is the code.")]
    [SerializeField] private bool randomizeEachRun;

    [Tooltip("Keep it at 1 or more while a note spells the digits out: \"0 workers\" and the " +
             "\"0th floor\" do not read.")]
    [SerializeField, Range(0, 9)] private int minDigit = 1;
    [SerializeField, Range(0, 9)] private int maxDigit = 9;

    [Tooltip("Played (2D) when the right sequence is entered. Empty = silent — for a code that only " +
             "opens the way to the real step, whose completion is the one that sounds.")]
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_1_completo";

    // This run's code, and the seed it was drawn from. Never serialized: it is derived again from
    // RunSeed whenever the seed changes.
    [NonSerialized] private List<int> runSequence;
    [NonSerialized] private int runSequenceSeed;

    public string PuzzleId => puzzleId;
    public string CompletedSoundId => completedSoundId;
    public string RequiredSocketId => requiredSocketId;
    public string PromptText => promptText;
    public SO_InventoryItem RewardItem => rewardItem;
    public IReadOnlyList<int> CorrectSequence => randomizeEachRun ? RunSequence() : correctSequence;

    private IReadOnlyList<int> RunSequence()
    {
        int seed = RunSeed.Value;
        if (runSequence != null && runSequenceSeed == seed) return runSequence;

        System.Random random = RunSeed.RandomFor(puzzleId);
        int min = Mathf.Min(minDigit, maxDigit);
        int max = Mathf.Max(minDigit, maxDigit);

        runSequence = new List<int>(correctSequence.Count);
        for (int i = 0; i < correctSequence.Count; i++)
            runSequence.Add(random.Next(min, max + 1));
        runSequenceSeed = seed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[{nameof(SO_SequencePuzzleData)}] {puzzleId}: this run's code is {string.Join("", runSequence)}.");
#endif
        return runSequence;
    }

    /// <summary>
    /// The code's placeholders for a clue note, counted from 1: <c>{1}</c> is the first digit,
    /// <c>{1:worker|workers}</c> the word that agrees with it, <c>{4:ord}</c> the fourth as an
    /// ordinal ("2nd").
    /// </summary>
    public string ResolveClue(string key, string format)
    {
        if (!int.TryParse(key, out int position)) return null;

        IReadOnlyList<int> code = CorrectSequence;
        if (position < 1 || position > code.Count) return null;

        int digit = code[position - 1];
        if (string.IsNullOrEmpty(format)) return digit.ToString();
        if (format == "ord") return ClueText.Ordinal(digit);
        return ClueText.Plural(digit, format);
    }
}
