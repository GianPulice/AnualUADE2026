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
    [Tooltip("Number of buttons the UI shows. IDs run from 1 to buttonCount.")]
    [SerializeField, Min(1)] private int buttonCount = 8;

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

        if (PuzzleStateManager.Instance != null &&
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
            (PuzzleStateManager.Instance == null ||
             !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId)))
            return "The fuse still needs to be inserted";
        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (sequenceData == null) return false;
        if (isCompleted) return false;

        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            (PuzzleStateManager.Instance == null ||
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

        SequencePanelUIController.Instance.Open(this);
    }

    public override bool IsRepeatable()
    {
        return !isCompleted;
    }

    /// <summary>
    /// Called from the UI when the player presses a button on the panel.
    /// Returns true if the button is correct at the current position.
    /// </summary>
    public bool TryPressButton(int buttonId)
    {
        if (isCompleted) return false;
        if (sequenceData == null) return false;

        IReadOnlyList<int> correct = sequenceData.CorrectSequence;
        int step = currentSequence.Count;

        if (step >= correct.Count) return false;

        if (correct[step] != buttonId)
        {
            currentSequence.Clear();
            OnButtonPressed?.Invoke(buttonId);
            OnSequenceFailed?.Invoke();
            return false;
        }

        currentSequence.Add(buttonId);
        OnButtonPressed?.Invoke(buttonId);

        if (currentSequence.Count == correct.Count)
        {
            CompleteSequence();
        }

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

        if (PuzzleStateManager.Instance != null)
            PuzzleStateManager.Instance.SetPuzzleCompleted(sequenceData.PuzzleId);

        if (sequenceData.RewardItem != null && InventoryManager.Instance != null)
            InventoryManager.Instance.AddItem(sequenceData.RewardItem);

        OnSequenceCompleted?.Invoke();

        Debug.Log($"[SequencePanel] Puzzle completed: {sequenceData.PuzzleId}");
    }
}
