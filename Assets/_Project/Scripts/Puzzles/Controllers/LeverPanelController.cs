using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The lever panel of the boiler room (Central Puzzle 2 — SP1). Its <see cref="LeverInteractable"/>
/// children are levers 1, 2, 3… in hierarchy order; the ones past the asset's Lever Count are
/// hidden, so the same prefab serves any sequence.
///
/// Every pull is recorded, and the attempt is judged once it is as long as the sequence — the same
/// rule as the Puzzle 1 sequence panel, so a failure never says which pull was wrong. Wrong: the
/// record is cleared, an error sound plays, nothing else (no penalty). Right: the puzzle completes.
/// The record itself is not saved: a half-entered attempt is worth nothing after a capture.
/// </summary>
public class LeverPanelController : MonoBehaviour
{
    [SerializeField] private SO_LeverSequencePuzzleData puzzleData;

    [Tooltip("Where the reward lands if it can be neither boxed nor carried. Empty = in front of the player.")]
    [SerializeField] private Transform rewardDropPoint;

    private readonly List<int> entered = new List<int>();
    private LeverInteractable[] levers = new LeverInteractable[0];

    public SO_LeverSequencePuzzleData PuzzleData => puzzleData;

    public bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    private void Awake()
    {
        if (puzzleData == null)
        {
            Debug.LogError($"[{nameof(LeverPanelController)}] No SO_LeverSequencePuzzleData on '{name}'.", this);
            return;
        }

        levers = GetComponentsInChildren<LeverInteractable>(true);
        if (levers.Length < puzzleData.LeverCount)
            Debug.LogWarning($"[{nameof(LeverPanelController)}] '{name}' has {levers.Length} levers but " +
                             $"'{puzzleData.name}' asks for {puzzleData.LeverCount}.", this);

        for (int i = 0; i < levers.Length; i++)
        {
            bool used = i < puzzleData.LeverCount;
            levers[i].Bind(this, i + 1);
            levers[i].gameObject.SetActive(used);
            if (used) WriteLetter(levers[i].transform, i + 1);
        }
    }

    /// <summary>
    /// Writes the lever's letter (A, B, C…) on the plate under it, in the lever's colour, the same
    /// ones the boiler's screen shows when it knocks that lever. Reuses a "Label" text if the lever
    /// still has one; otherwise makes one just in front of its "LabelPlate".
    /// </summary>
    private void WriteLetter(Transform lever, int number)
    {
        TextMeshPro text = null;
        Transform existing = lever.Find("Label");
        if (existing != null) text = existing.GetComponent<TextMeshPro>();

        if (text == null)
        {
            Transform plate = FindDeep(lever, "LabelPlate");
            if (plate == null) return;

            var go = new GameObject("Label", typeof(RectTransform));
            go.layer = lever.gameObject.layer;
            go.transform.SetParent(lever, false);
            go.transform.rotation = lever.rotation;
            // The plate is a cube: its front is half its depth towards -Z; a hair more clears it.
            Vector3 front = plate.TransformPoint(new Vector3(0f, 0f, -0.5f));
            go.transform.position = front - lever.forward * 0.004f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(0.18f, 0.14f);

            text = go.AddComponent<TextMeshPro>();
            text.fontSize = 1.6f;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        text.text = LeverSymbols.Letter(number);
        text.color = LeverSymbols.ColorOf(number);
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    /// <summary>A lever was pulled. Judges the attempt once it is complete.</summary>
    public void RegisterPull(int lever)
    {
        if (puzzleData == null || IsCompleted) return;

        SO_LeverSequencePuzzleData.Knock[] sequence = puzzleData.Sequence;
        if (sequence.Length == 0) return;

        entered.Add(lever);
        if (entered.Count < sequence.Length) return;

        bool correct = true;
        for (int i = 0; i < sequence.Length; i++)
        {
            if (entered[i] != sequence[i].lever) { correct = false; break; }
        }
        entered.Clear();

        if (correct) Complete();
        else if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.WrongSequenceSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.WrongSequenceSoundId, transform.position);
    }

    private void Complete()
    {
        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(LeverPanelController)}] No PuzzleStateManager — completing " +
                             $"'{puzzleData.PuzzleId}' was not recorded.", this);
            return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);

        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.CompletedSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.CompletedSoundId);   // 2D: heard alike from anywhere

        // The component for the hub, as Zona 1's sub-puzzles hand out theirs: to this puzzle's box.
        PuzzleRewardDelivery.Deliver(puzzleData.PuzzleId, puzzleData.RewardItem, rewardDropPoint, this);

        Debug.Log($"[{nameof(LeverPanelController)}] Puzzle completed: {puzzleData.PuzzleId}");
    }
}
