using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sequence panel (puzzle SP1). Inherits from BaseRangeInteractable to use the same
/// collider-based detection flow as the rest of the interactables.
/// On interact, it opens the SequencePanelUIController UI. The puzzle logic lives here;
/// the UI is only view + input.
/// </summary>
public class SequencePanelInteractable : BaseRangeInteractable
{
    [Header("Puzzle data")]
    [SerializeField] private SO_SequencePuzzleData sequenceData;

    [Header("Panel configuration")]
    [Tooltip("Number of numbered keys the UI shows: IDs run from 1 to buttonCount. " +
             "The keypad always adds the 0 in its own row below, so 9 gives the usual layout.")]
    [SerializeField, Min(1)] private int buttonCount = 9;

    private readonly List<int> currentSequence = new List<int>();
    private bool isCompleted;

    public string PuzzleId => sequenceData != null ? sequenceData.PuzzleId : string.Empty;
    public int ButtonCount => buttonCount;
    public IReadOnlyList<int> CorrectSequence =>
        sequenceData != null ? sequenceData.CorrectSequence : new List<int>();
    public IReadOnlyList<int> EnteredSequence => currentSequence;
    public bool IsCompleted => isCompleted;

    /// <summary>Raised when the player enters a button (correct or incorrect).</summary>
    public event Action<int> OnButtonPressed;
    /// <summary>Raised when the entered sequence is incorrect (resets the input).</summary>
    public event Action OnSequenceFailed;
    /// <summary>Raised when the sequence is completed correctly.</summary>
    public event Action OnSequenceCompleted;

    protected override void Awake()
    {
        base.Awake();

        if (sequenceData == null)
        {
            Debug.LogError($"SequencePanelInteractable without SO_SequencePuzzleData on {gameObject.name}");
            return;
        }

        // Exists rather than 'Instance != null': the property logs a warning of its own every time
        // it is read while null, so testing it as a null check spams the console on the very setup
        // it is meant to tolerate. Same swap in the three places below.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(sequenceData.PuzzleId))
        {
            isCompleted = true;
        }
    }

    public override string GetInteractText()
    {
        if (sequenceData == null) return "Unconfigured panel";
        if (isCompleted) return string.Empty;
        return sequenceData.PromptText;
    }

    public override string GetInfoText()
    {
        if (sequenceData == null || isCompleted) return string.Empty;
        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            (!PuzzleStateManager.Exists ||
             !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId)))
            return "The fuse still needs to be inserted";
        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (sequenceData == null) return false;
        if (isCompleted) return false;

        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            (!PuzzleStateManager.Exists ||
             !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId)))
        {
            return false;
        }

        return true;
    }

    protected override void OnInteract()
    {
        if (SequencePanelUIController.Instance == null)
        {
            Debug.LogError("[SequencePanelInteractable] There is no SequencePanelUIController in the scene (LevelUI).");
            return;
        }

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_interaction_panel_electrico", transform.position);

        SequencePanelUIController.Instance.Open(this);
    }

    public override bool IsRepeatable()
    {
        return !isCompleted;
    }

    /// <summary>
    /// Called from the UI when the player presses a button on the panel. Every key is accepted:
    /// the attempt is only judged once it is as long as the correct sequence.
    ///
    /// Validating key by key was giving the code away — a wrong key failed on the spot, so the
    /// player could try 1, 2, 3… until one of them did not fail and brute force the panel one
    /// digit at a time. Judging the complete attempt means a failure says nothing about
    /// *which* key was wrong.
    ///
    /// Returns false only when the completed attempt was incorrect.
    /// </summary>
    public bool TryPressButton(int buttonId)
    {
        if (isCompleted) return false;
        if (sequenceData == null) return false;

        IReadOnlyList<int> correct = sequenceData.CorrectSequence;
        if (correct.Count == 0) return false;   // Unconfigured puzzle: nothing to match.

        currentSequence.Add(buttonId);
        OnButtonPressed?.Invoke(buttonId);

        // Attempt still in progress: no feedback beyond the key lighting up.
        if (currentSequence.Count < correct.Count) return true;

        if (MatchesCorrectSequence())
        {
            CompleteSequence();
            return true;
        }

        currentSequence.Clear();
        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_secuencia_incorrecta_panel_electrico");
        OnSequenceFailed?.Invoke();
        return false;
    }

    private bool MatchesCorrectSequence()
    {
        IReadOnlyList<int> correct = sequenceData.CorrectSequence;
        if (currentSequence.Count != correct.Count) return false;

        for (int i = 0; i < correct.Count; i++)
            if (currentSequence[i] != correct[i]) return false;

        return true;
    }

    public void ResetSequence()
    {
        if (currentSequence.Count == 0) return;
        currentSequence.Clear();
    }

    private void CompleteSequence()
    {
        isCompleted = true;

        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.SetPuzzleCompleted(sequenceData.PuzzleId);
        else
            Debug.LogWarning($"[{nameof(SequencePanelInteractable)}] No PuzzleStateManager — " +
                             $"completing '{sequenceData.PuzzleId}' was not recorded, so nothing " +
                             $"gated behind it will open.", this);

        if (sequenceData.RewardItem != null)
        {
            if (InventoryManager.Exists) InventoryManager.Instance.AddItem(sequenceData.RewardItem);
            else Debug.LogWarning($"[{nameof(SequencePanelInteractable)}] No InventoryManager — " +
                                  $"the reward '{sequenceData.RewardItem.name}' for " +
                                  $"'{sequenceData.PuzzleId}' was not granted.", this);
        }

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_subpuzzle_completo");

        OnSequenceCompleted?.Invoke();

        Debug.Log($"[SequencePanel] Puzzle completed: {sequenceData.PuzzleId}");
    }
}
